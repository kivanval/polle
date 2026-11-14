import '@geoman-io/leaflet-geoman-free'
import { useEffect, useEffectEvent } from 'react'
import { useMap } from 'react-leaflet'
import type { LatLng, LeafletMouseEvent, Polygon } from 'leaflet'

const drawStyle = { color: '#d6ed83', weight: 2, fillColor: '#d6ed83', fillOpacity: 0.18 }
const CLOSE_RADIUS_PX = 12

// Geoman's polygon drawer. Its finishing helpers are not part of the typed API.
type Drawer = { enabled(): boolean; _layer?: Polygon; _finishShape(): void; _removeLastVertex(): void }

/** While active, the map is in polygon-drawing mode. Self-intersections are blocked and vertices snap to nearby fields. */
export function DrawPolygon({ active, onDrawn, onCancel }: { active: boolean; onDrawn: (latLngs: LatLng[]) => void; onCancel: () => void }) {
  const map = useMap()
  const drawn = useEffectEvent(onDrawn)
  const cancel = useEffectEvent(onCancel)

  useEffect(() => {
    if (!active) return
    map.pm.enableDraw('Polygon', {
      snappable: true, snapDistance: 14, allowSelfIntersection: false, tooltips: true, finishOnEnter: true,
      templineStyle: { color: '#d6ed83', weight: 2 }, hintlineStyle: { color: '#f4f3eb', weight: 1.5, dashArray: '4 6' },
      pathOptions: drawStyle,
    })
    const created = ({ layer }: { layer: unknown }) => {
      const polygon = layer as Polygon
      const ring = polygon.getLatLngs()[0] as LatLng[]
      polygon.remove()
      drawn(ring)
    }
    // Geoman only closes the ring when the click lands exactly on the first corner. When that corner was snapped to a
    // neighbouring field, the snapped click lands a hair away and is silently dropped. Close it ourselves instead.
    const drawer = (map.pm.Draw as unknown as { Polygon: Drawer }).Polygon
    const closeNearFirst = (event: LeafletMouseEvent) => {
      const ring = drawer._layer?.getLatLngs() as LatLng[] | undefined
      if (!drawer.enabled() || !ring || ring.length < 3) return
      const near = (point: LatLng) => map.latLngToContainerPoint(point).distanceTo(event.containerPoint) <= CLOSE_RADIUS_PX
      if (!near(ring[0])) return
      if (ring.length > 3 && near(ring[ring.length - 1])) drawer._removeLastVertex()
      drawer._finishShape()
    }
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') cancel() }
    map.on('click', closeNearFirst)
    map.on('pm:create', created)
    window.addEventListener('keydown', escape)
    return () => {
      map.off('pm:create', created)
      map.off('click', closeNearFirst)
      window.removeEventListener('keydown', escape)
      map.pm.disableDraw()
    }
  }, [active, map])

  return null
}
