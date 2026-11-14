import type { CSSProperties, ReactNode } from 'react'
import { ChevronDown, ChevronUp, X } from 'lucide-react'
import { coordinates, type ReadingBadge } from './focus'

/**
 * The selected place's card title with its collapse and clear buttons. Collapsed, it keeps the title and a one-word
 * reading (`badge`); the title, like the chevron, opens it again. Clearing is the only way to drop the place.
 */
export function ClimateFocusHead({ picked, collapsed, badge, details, onCollapse, onClear }: {
  picked: { lat: number; lng: number } | null; collapsed: boolean; badge: ReadingBadge
  /** The month, its kind and the layer, shown while the card is open. */
  details: ReactNode
  onCollapse: (collapsed: boolean) => void; onClear: () => void
}) {
  const folded = collapsed && picked !== null
  const place = picked && <>Selected: <span className="mono">{coordinates(picked.lat, picked.lng)}</span></>
  return <header className="climate-focus-head">
    <div>
      <h2>{!picked ? 'No place selected'
        // The chevron is the keyboard's way back; the title is a larger target for the pointer.
        : folded ? <button type="button" className="climate-focus-title" tabIndex={-1} onClick={() => onCollapse(false)}>{place}</button> : place}</h2>
      {folded
        ? <p><span className="climate-focus-badge" style={badge.color ? { '--verdict': badge.color } as CSSProperties : undefined}>
            {badge.color && <span className="climate-focus-badge-swatch" aria-hidden="true" />}{badge.label}</span></p>
        : <p>{details}</p>}
    </div>
    {picked && <div className="climate-focus-actions">
      <button type="button" className="drought-step" aria-expanded={!folded} aria-controls="climate-focus-body"
        aria-label={folded ? 'Expand the selected place' : 'Collapse the selected place'} title={folded ? 'Expand' : 'Collapse'}
        onClick={() => onCollapse(!folded)}>{folded ? <ChevronDown size={15} /> : <ChevronUp size={15} />}</button>
      <button type="button" className="drought-step" aria-label="Clear the selected place" title="Clear" onClick={onClear}><X size={15} /></button>
    </div>}
  </header>
}
