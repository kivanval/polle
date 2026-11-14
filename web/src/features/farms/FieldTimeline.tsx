import { barLayout, type TimelineRange } from './timeline'

/**
 * `open`: no harvest date yet, drawn fading out. `warnings`: risk notes and `cautions`: warning notes, each listed under
 * its own heading on hover; either marks the bar.
 */
export type TimelineBar = { key: string; label: string; detail: string; from: string; to: string | null; colour: string; open?: boolean
  warnings?: string[]; cautions?: string[]; onClick?: () => void }
export type TimelineLane = { key: string; label?: string; bars: TimelineBar[] }

/**
 * Crops on a time axis: one column per year, a bar per crop, a line for today. Empty stretches between bars are
 * when the field is bare. Compact lanes are thin strips without labels, used inside cards and lists.
 */
export function FieldTimeline({ lanes, range, today, compact = false, label }: {
  lanes: TimelineLane[]; range: TimelineRange; today?: string; compact?: boolean; label: string
}) {
  const firstYear = Number(range.start.slice(0, 4))
  const years = Array.from({ length: Number(range.end.slice(0, 4)) - firstYear + 1 }, (_, index) => firstYear + index)
  const todayLeft = today && today >= range.start && today <= range.end ? barLayout(range, today, today).left : null
  return <div className={`timeline ${compact ? 'is-compact' : ''}`} role="group" aria-label={label} style={{ '--timeline-years': years.length } as React.CSSProperties}>
    {!compact && <div className="timeline-years" aria-hidden="true">{years.map(year => <span key={year} className="mono">{year}</span>)}</div>}
    {lanes.map(lane => <div key={lane.key} className="timeline-lane">
      {lane.label && <div className="timeline-lane-label">{lane.label}</div>}
      <div className="timeline-track">
        {todayLeft !== null && !compact && <span className="timeline-today" style={{ left: `${todayLeft}%` }} aria-hidden="true" />}
        {lane.bars.map(bar => {
          const { left, width } = barLayout(range, bar.from, bar.to)
          const style = { left: `${left}%`, width: `${width}%`, background: bar.colour }
          const text = `${bar.label}, ${bar.detail}${bar.warnings?.length ? `\nRisks:\n• ${bar.warnings.join('\n• ')}` : ''}${bar.cautions?.length ? `\nWarnings:\n• ${bar.cautions.join('\n• ')}` : ''}`
          const className = `timeline-bar${bar.open ? ' is-open' : ''}${bar.warnings?.length || bar.cautions?.length ? ' has-risk' : ''}`
          return bar.onClick
            ? <button key={bar.key} type="button" className={className} style={style} aria-label={text} title={text} onClick={bar.onClick}>{!compact && bar.label}</button>
            : <span key={bar.key} className={className} style={style} title={text} role="img" aria-label={text}>{!compact && bar.label}</span>
        })}
      </div>
    </div>)}
  </div>
}
