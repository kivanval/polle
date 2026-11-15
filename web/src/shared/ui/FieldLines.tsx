import { useId, type CSSProperties } from 'react'
import { fieldBoundaries, fieldLabel, imageHeight, imageWidth } from './fieldBoundaries'

type FieldLinesProps = {
  className?: string
  /** Satellite pass that sweeps the image once; outlines trace as it reaches them. */
  scan?: boolean
  /** Subset of boundary ids to draw; all boundaries when omitted. */
  only?: readonly string[]
  /** Framing within the image; must keep the viewport's aspect ratio or it falls back to a centred cover. */
  viewBox?: string
  /** Makes outlines focusable and selectable; omitted, the drawing stays decorative. */
  onSelect?: (id: string) => void
  selected?: string
}

/** Image and curated outlines share one cover transform, including on narrow screens. */
export function FieldLines({ className = '', scan = false, only, viewBox = `0 0 ${imageWidth} ${imageHeight}`, onSelect, selected }: FieldLinesProps) {
  const swath = useId()
  const shown = only ? fieldBoundaries.filter(field => only.includes(field.id)) : fieldBoundaries
  return <svg className={'field-lines ' + className} viewBox={viewBox} preserveAspectRatio="xMidYMid slice" fill="none"
    {...onSelect ? { role: 'group', 'aria-label': 'Aerial photograph of farmland in Biei, Hokkaido, with example field outlines, not live geospatial data' }
      : { role: 'img', 'aria-label': 'Aerial photograph of farmland in Biei, Hokkaido, with decorative field outlines, not live geospatial data' }}>
    <image href="/images/field-atlas.webp" width={imageWidth} height={imageHeight} />
    <g className="field-outlines" stroke="currentColor" strokeWidth="1.7" strokeLinejoin="round">
      {shown.map(({ id, points }) => {
        // Each outline starts tracing when the scan line reaches its northernmost vertex.
        const top = Math.min(...points.map(([, y]) => y)) / imageHeight
        return <g className="field" key={id} style={{ '--reach': top.toFixed(3) } as CSSProperties}
          {...onSelect && { tabIndex: 0, role: 'button', 'aria-label': fieldLabel(id), 'aria-pressed': selected === id,
            onClick: () => onSelect(id), onFocus: () => onSelect(id),
            onKeyDown: event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onSelect(id) } } }}>
          <path vectorEffect="non-scaling-stroke" pathLength="1" d={'M' + points.map(point => point.join(' ')).join(' ') + 'Z'} />
          {points.map(([x, y]) => <circle key={`${x}-${y}`} className="vertex" cx={x} cy={y} r="4.5" />)}
        </g>
      })}
    </g>
    {scan && <g className="scan" aria-hidden="true">
      <linearGradient id={swath} x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stopColor="currentColor" stopOpacity="0" />
        <stop offset="1" stopColor="currentColor" stopOpacity=".16" />
      </linearGradient>
      <rect y="-180" width={imageWidth} height="180" fill={`url(#${swath})`} />
      <rect width={imageWidth} height="2" fill="currentColor" />
    </g>}
  </svg>
}
