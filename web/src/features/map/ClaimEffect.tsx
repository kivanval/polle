import { useEffect, useRef, useState } from 'react'
import { Marker, Polygon, Tooltip } from 'react-leaflet'
import type { Polygon as LeafletPolygon } from 'leaflet'
import type { PolygonGeometry } from '@/features/farms/api'
import { centre, formatHectares, toLatLngs } from '@/features/farms/geo'
import { claimRipple } from './markers'

/**
 * The moment a boundary closes: the outline traces itself, the field floods with lime from its centre,
 * and the hectare count rolls up. Under reduced motion the final state shows immediately.
 */
export function ClaimEffect({ boundary, hectares }: { boundary: PolygonGeometry; hectares: number }) {
  const ref = useRef<LeafletPolygon>(null)
  useEffect(() => {
    // pathLength=1 lets CSS animate the stroke without measuring the path.
    ref.current?.getElement()?.setAttribute('pathLength', '1')
  }, [boundary])
  return <>
    <Polygon ref={ref} positions={toLatLngs(boundary)} interactive={false} className="field-claim"
      pathOptions={{ color: '#d6ed83', weight: 3, fillColor: '#d6ed83', fillOpacity: 0.3 }} pmIgnore snapIgnore>
      <Tooltip permanent direction="center" className="claim-tooltip" opacity={1}><CountUp to={hectares} /></Tooltip>
    </Polygon>
    <Marker position={centre(boundary)} icon={claimRipple} interactive={false} keyboard={false} pmIgnore snapIgnore />
  </>
}

/** Flashes the conflicting area in red and outlines the rejected shape. */
export function RejectEffect({ boundary, conflict }: { boundary: PolygonGeometry; conflict: PolygonGeometry[] }) {
  return <>
    <Polygon positions={toLatLngs(boundary)} interactive={false} className="field-reject"
      pathOptions={{ color: '#ff8a7a', weight: 2, dashArray: '6 6', fillOpacity: 0.06, fillColor: '#ff8a7a' }} pmIgnore snapIgnore />
    {conflict.map((shape, index) => <Polygon key={index} positions={toLatLngs(shape)} interactive={false} className="field-conflict"
      pathOptions={{ color: '#a23029', weight: 1.5, fillColor: '#e2493b', fillOpacity: 0.55 }} pmIgnore snapIgnore />)}
  </>
}

function CountUp({ to }: { to: number }) {
  const [reduced] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches)
  const [value, setValue] = useState(reduced ? to : 0)
  useEffect(() => {
    if (reduced) return
    let frame = 0
    const start = performance.now()
    const tick = (now: number) => {
      const progress = Math.min(1, (now - start - 450) / 900)
      setValue(to * (1 - Math.pow(1 - Math.max(0, progress), 3)))
      if (progress < 1) frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [to, reduced])
  return <span className="claim-count">{formatHectares(reduced ? to : value)}</span>
}
