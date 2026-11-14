/** Side view of a tractor pulling a plough, facing right. Wheels turn and the exhaust puffs while loading. */
function TractorArt({ size, label }: { size: number; label?: string }) {
  return <svg className="tractor-art" width={size} height={size * 36 / 56} viewBox="0 0 56 36" fill="none"
    {...(label ? { role: 'img', 'aria-label': label } : { 'aria-hidden': true })}>
    <g className="tractor-smoke"><circle cx="41" cy="3" r="1.6" /><circle cx="43" cy="1.5" r="1.2" /></g>
    <path className="tractor-plough" d="M14 24 L5 32.5 M3 31.5 L9 34.5" />
    <rect className="tractor-body" x="40" y="5" width="2.6" height="9" rx="1" />
    <rect className="tractor-body" x="12" y="2.5" width="20" height="3" rx="1.2" />
    <rect className="tractor-body" x="14" y="4" width="16" height="19" rx="2" />
    <rect className="tractor-glass" x="17" y="7" width="10" height="8" rx="1" />
    <rect className="tractor-body" x="28" y="13" width="22" height="10" rx="2.5" />
    <path className="tractor-trim" d="M47 15.5 V20.5" />
    <g className="tractor-wheel"><circle cx="20" cy="27" r="8" /><circle className="tractor-hub" cx="20" cy="27" r="3.4" />
      <path className="tractor-spokes" d="M20 19.6 V22.6 M20 31.4 V34.4 M12.6 27 H15.6 M24.4 27 H27.4" /></g>
    <g className="tractor-wheel is-front"><circle cx="45" cy="30" r="5" /><circle className="tractor-hub" cx="45" cy="30" r="2" />
      <path className="tractor-spokes" d="M45 25.4 V27.2 M45 32.8 V34.6 M40.4 30 H42.2 M47.8 30 H49.6" /></g>
  </svg>
}

/** Full loading state: a tractor ploughs furrows across a strip of field. Decorative; pair it with a text status. */
export function TractorLoader({ compact = false }: { compact?: boolean }) {
  return <span className={`tractor-loader ${compact ? 'is-compact' : ''}`} aria-hidden="true">
    <span className="tractor-field" />
    <span className="tractor-furrows" />
    <span className="tractor-driver"><TractorArt size={56} /></span>
  </span>
}

/** Inline busy mark for buttons and fields, in place of a spinning circle. */
export function TractorSpinner({ size = 15, label }: { size?: number; label?: string }) {
  return <span className="tractor-spinner" style={{ width: size * 1.35 }}><TractorArt size={size * 1.35} label={label} /></span>
}
