import { useEffect, useMemo, useRef } from 'react'
import { Marker, Pane, Polygon, Tooltip, useMap, useMapEvents } from 'react-leaflet'
import { latLngBounds, type LatLngBoundsExpression, type LatLngBoundsLiteral, type PathOptions, type LatLngLiteral, type LatLngTuple, type Polygon as LeafletPolygon } from 'leaflet'
import { House } from 'lucide-react'
import { MapControl } from '@/features/map/BaseMap'
import { homeIcon } from '@/features/map/markers'
import type { Field } from './api'
import { formatHectares, toLatLngs } from './geo'

export type ShapeState = 'idle' | 'hovered' | 'selected' | 'dimmed'

const shapeStyles: Record<ShapeState, PathOptions> = {
  idle: { color: '#d6ed83', weight: 2, opacity: 0.95, fillColor: '#d6ed83', fillOpacity: 0.14 },
  hovered: { color: '#e9f6b8', weight: 3, opacity: 1, fillColor: '#d6ed83', fillOpacity: 0.26 },
  selected: { color: '#f4f3eb', weight: 3, opacity: 1, fillColor: '#d6ed83', fillOpacity: 0.34 },
  dimmed: { color: '#d6ed83', weight: 1.5, opacity: 0.6, fillColor: '#d6ed83', fillOpacity: 0.07 },
}

type FieldShapeProps = {
  field: Field; state: ShapeState; editing: boolean; cropName?: string
  onSelect: (fieldId: string) => void; onHover: (fieldId: string | null) => void
  register: (fieldId: string, layer: LeafletPolygon | null) => void
}

export function FieldShape({ field, state, editing, cropName, onSelect, onHover, register }: FieldShapeProps) {
  const ref = useRef<LeafletPolygon>(null)
  useEffect(() => {
    const layer = ref.current
    register(field.id, layer)
    return () => register(field.id, null)
  }, [field.id, register])
  useEffect(() => {
    const layer = ref.current
    if (!layer || !editing) return
    layer.pm.enable({ allowSelfIntersection: false, snappable: true, snapDistance: 14, preventMarkerRemoval: false })
    return () => layer.pm.disable()
  }, [editing])
  // react-leaflet calls setLatLngs whenever `positions` is a new array, which throws away the corners Geoman is
  // moving. Keep the same array until the saved boundary itself changes, so a re-render mid-edit leaves the shape be.
  const positions = useMemo(() => toLatLngs(field.boundary), [field.boundary])
  const style = editing ? shapeStyles.selected : shapeStyles[state]
  // Subfields are drawn dashed on top of their parent, so the division reads as part of the field, not a separate one.
  const pathOptions = field.parentFieldId ? { ...style, dashArray: '6 5', fillOpacity: (style.fillOpacity ?? 0) * 0.7 } : style
  return <Polygon ref={ref} positions={positions} pathOptions={pathOptions}
    // Clicks must still reach the map: drawing and point placement listen there, including on top of existing fields.
    className="field-shape"
    // While its corners are dragged the pointer keeps crossing the outline; hover must not re-render the page then.
    eventHandlers={{ click: () => onSelect(field.id), mouseover: () => { if (!editing) onHover(field.id) }, mouseout: () => { if (!editing) onHover(null) } }}>
    {/* Always permanent, shown from our hover/selection state. A non-permanent Leaflet tooltip makes the path focusable
        and leaves a blur listener behind that later closes the selected field's label (and Chrome boxes the focus). */}
    {(state === 'selected' || state === 'hovered') && !editing && <Tooltip permanent direction="center" className="field-tooltip" opacity={1}>
      <strong>{field.name}</strong>{cropName && <em>{cropName}</em>}<span>{formatHectares(field.areaHectares)}</span>
    </Tooltip>}
  </Polygon>
}

export function HomeMarker({ position, label, movable, onMove }: { position: LatLngTuple | LatLngLiteral; label: string; movable: boolean; onMove: (position: LatLngLiteral) => void }) {
  return <Marker position={position} icon={homeIcon({ moving: movable, label })} draggable={movable} autoPan
    title={movable ? 'Drag to move the farm point' : label} keyboard={false} snapIgnore
    eventHandlers={{ dragend: event => onMove(event.target.getLatLng()) }} />
}

/** Lets a map click place the farm point. */
export function PointPicker({ onPick }: { onPick: (position: LatLngLiteral) => void }) {
  useMapEvents({ click: event => onPick(event.latlng) })
  return null
}

/**
 * Brings the given bounds into view when they change. A field that is already fully visible and big enough on screen
 * keeps the camera still: clicking a field on the map should select it, not jolt the map.
 */
/** How far the docked field inspector reaches into the map from its left edge, in pixels; 0 when it isn't over the map. */
function leftCover(container: HTMLElement) {
  const inspector = document.getElementById('field-inspector')
  if (!inspector) return 0
  const map = container.getBoundingClientRect(), panel = inspector.getBoundingClientRect()
  const overlaps = panel.left < map.right && panel.right > map.left && panel.top < map.bottom && panel.bottom > map.top
  // Only a side panel counts; one as wide as the map is a sheet below or over it, not a left inset.
  return overlaps && panel.width < map.width * 0.6 ? Math.max(0, panel.right - map.left) : 0
}

export function FlyTo({ bounds }: { bounds: LatLngBoundsExpression | null }) {
  const map = useMap()
  useEffect(() => {
    if (!bounds) return
    const target = latLngBounds(bounds as LatLngBoundsLiteral)
    const fit = () => {
      const southEast = map.latLngToContainerPoint(target.getSouthEast()), northWest = map.latLngToContainerPoint(target.getNorthWest())
      const size = southEast.subtract(northWest)
      // An open layers panel covers the map's right side (or, on small maps, its bottom as a sheet);
      // keep the field in the part of the map that stays visible.
      const panel = map.getContainer().querySelector<HTMLElement>('.farm-drought.is-open')
      const sheet = Boolean(panel && panel.offsetWidth >= map.getSize().x * 0.6)
      const right = panel && !sheet ? panel.offsetWidth + 14 : 0
      const bottom = panel && sheet ? panel.offsetHeight + 26 : 0
      // The field inspector docks over the map's left side on wide screens (below the map on small ones, where it covers nothing).
      const left = leftCover(map.getContainer())
      const inView = map.getBounds().pad(-0.05).contains(target) && northWest.x >= left + 12
        && southEast.x <= map.getSize().x - right - 12 && southEast.y <= map.getSize().y - bottom - 12
      if (inView && size.x >= 48 && size.y >= 48) return
      const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      map.flyToBounds(target, { paddingTopLeft: [60 + left, 60], paddingBottomRight: [60 + right, 60 + bottom], maxZoom: 17, duration: reduce ? 0 : 0.9 })
    }
    fit()
    // Opening the layers panel after selecting a field moves the field out from under it.
    map.on('layerspanelopen', fit)
    return () => { map.off('layerspanelopen', fit) }
  }, [bounds, map])
  return null
}

/**
 * Soft lime glow around the selected field, drawn as a wide stroke in its own pane under the fields.
 * (A CSS drop-shadow on the SVG path drifts away from the shape while Leaflet animates a zoom.)
 */
export function SelectionHalo({ field }: { field: Field | null }) {
  return <Pane name="field-halo" style={{ zIndex: 390 }}>
    {field && <Polygon key={`${field.id}:${field.updatedAtUtc}`} positions={toLatLngs(field.boundary)} interactive={false} pmIgnore snapIgnore
      pathOptions={{ color: '#d6ed83', weight: 12, opacity: 0.45, fill: false, lineJoin: 'round' }} />}
  </Pane>
}

export function RecenterControl({ home }: { home: LatLngTuple | null }) {
  const map = useMap()
  if (!home) return null
  return <MapControl className="map-recenter">
    <button type="button" aria-label="Return to the farm point" title="Return to the farm point" onClick={() => map.flyTo(home, 16, { duration: 0.9 })}>
      <House size={16} />
    </button>
  </MapControl>
}
