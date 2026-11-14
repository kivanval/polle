import { useEffect, useRef, useState, type ReactNode } from 'react'
import { MapContainer, TileLayer, useMap } from 'react-leaflet'
import { Minus, Plus } from 'lucide-react'
import { DomEvent, type LatLngBoundsExpression, type LatLngExpression } from 'leaflet'
import { env } from '@/shared/config/env'

const osmAttribution = '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
// CARTO basemaps now answer every tile without a key with an "API key required" image. With a key (free tier, sent to
// the browser by design) its Voyager style is used; otherwise the OpenStreetMap standard tiles, which need no key.
const cartoKey = env.cartoBasemapsKey
const streetMap = cartoKey
  ? { maxNativeZoom: 20, url: `https://{s}.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}{r}.png?key=${encodeURIComponent(cartoKey)}`,
      attribution: `${osmAttribution} © <a href="https://carto.com/attributions">CARTO</a>` }
  // tile.openstreetmap.org serves up to zoom 19 and asks for visible attribution and normal browser requests only.
  : { maxNativeZoom: 19, url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png', attribution: osmAttribution }
const basemaps = {
  satellite: {
    label: 'Satellite', maxNativeZoom: 18,
    url: 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}',
    attribution: 'Imagery © Esri, Maxar, Earthstar Geographics',
  },
  map: { label: 'Map', ...streetMap },
} as const
const placeLabels = 'https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}'
type Basemap = keyof typeof basemaps

type BaseMapProps = {
  center?: LatLngExpression; zoom?: number; bounds?: LatLngBoundsExpression
  className?: string; label: string; overlay?: ReactNode; children?: ReactNode
}

/** Leaflet map in the atlas style: satellite by default, quiet custom chrome, no Leaflet zoom control. */
export function BaseMap({ center = [48.6, 31.2], zoom = 6, bounds, className = '', label, overlay, children }: BaseMapProps) {
  const [basemap, setBasemap] = useState<Basemap>('satellite')
  return <div className={`atlas-map ${className}`}>
    <MapContainer center={bounds ? undefined : center} zoom={bounds ? undefined : zoom} bounds={bounds}
      boundsOptions={{ padding: [48, 48], maxZoom: 16 }} zoomControl={false} attributionControl
      className="atlas-map-canvas" aria-label={label} maxZoom={20} worldCopyJump>
      <TileLayer key={basemap} url={basemaps[basemap].url} attribution={basemaps[basemap].attribution} maxNativeZoom={basemaps[basemap].maxNativeZoom} maxZoom={20} />
      {basemap === 'satellite' && <TileLayer url={placeLabels} maxNativeZoom={18} maxZoom={20} opacity={0.85} />}
      <ZoomControls />
      {children}
    </MapContainer>
    <div className="map-basemaps" role="radiogroup" aria-label="Base map">
      {(Object.keys(basemaps) as Basemap[]).map(key => <button key={key} type="button" role="radio" aria-checked={basemap === key}
        onClick={() => setBasemap(key)}>{basemaps[key].label}</button>)}
    </div>
    {overlay}
  </div>
}

/** Wrapper for controls rendered inside the Leaflet container: clicks and scrolls must not reach the map. */
export function MapControl({ className, children }: { className: string; children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!ref.current) return
    DomEvent.disableClickPropagation(ref.current)
    DomEvent.disableScrollPropagation(ref.current)
  }, [])
  return <div ref={ref} className={className}>{children}</div>
}

function ZoomControls() {
  const map = useMap()
  return <MapControl className="map-zoom">
    <button type="button" aria-label="Zoom in" onClick={() => map.zoomIn()}><Plus size={16} /></button>
    <button type="button" aria-label="Zoom out" onClick={() => map.zoomOut()}><Minus size={16} /></button>
  </MapControl>
}
