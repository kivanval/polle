import { useEffect, useRef, useState } from 'react'
import { ChevronDown, ChevronRight, CircleAlert, TriangleAlert } from 'lucide-react'
import { cropColour } from '@/features/farms/timeline'
import { cycleStages, ganttScale, STAGE_LABEL, type GanttGroup, type Stage } from './details'
import { monthText, whenText } from './format'

const STAGES: Stage[] = ['sowing', 'growth', 'harvest']
/** Natural column widths in px; the label column falls back to `--gantt-label`'s desktop value in strategies.css. */
const MONTH_WIDTH = 30, YEAR_WIDTH = 60, LABEL_WIDTH = 210
const MONTH_INITIALS = ['J', 'F', 'M', 'A', 'M', 'J', 'J', 'A', 'S', 'O', 'N', 'D']

const plural = (count: number, one: string) => `${count} ${count === 1 ? one : `${one}s`}`

/**
 * A project-style Gantt chart of crops: one row per crop, a grid of years that open into months, and each crop's bar
 * split into sowing, growth and harvest blocks, told apart by colour alone (the legend names them). Every year, the
 * first included, starts collapsed; a click on a year or "Expand all months" opens the months. Groups (e.g. the
 * strategy, then the current plan) stack as sections.
 */
export function StrategyGantt({ groups, firstYear, lastYear, today, label }: {
  groups: GanttGroup[]; firstYear: number; lastYear: number; today?: string; label: string
}) {
  const [open, setOpen] = useState(() => new Set<number>())
  // Columns stretch to fill the panel when the years fit; otherwise the chart scrolls sideways at its natural size.
  const [available, setAvailable] = useState(0)
  const scroller = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const element = scroller.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() =>
      setAvailable(element.clientWidth - (parseFloat(getComputedStyle(element).getPropertyValue('--gantt-label')) || LABEL_WIDTH)))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  const natural = ganttScale(firstYear, lastYear, open, MONTH_WIDTH, YEAR_WIDTH)
  const stretch = Math.max(1, available / natural.width)
  const scale = stretch > 1 ? ganttScale(firstYear, lastYear, open, MONTH_WIDTH * stretch, YEAR_WIDTH * stretch) : natural
  const toggle = (year: number) => setOpen(current => { const next = new Set(current); if (!next.delete(year)) next.add(year); return next })
  const allOpen = scale.years.every(year => year.expanded)
  const todayMonth = today?.slice(0, 7)
  const todayX = todayMonth && todayMonth >= `${firstYear}-01` && todayMonth <= `${lastYear}-12`
    ? scale.x(todayMonth) + (Number(today!.slice(8, 10)) / 31) * (scale.x(todayMonth, true) - scale.x(todayMonth)) : null
  const track = { width: scale.width }
  const rows = groups.flatMap(group => group.rows)
  const anyRisk = rows.some(row => row.risks.length), anyWarning = rows.some(row => row.warnings.length)

  // Vertical lines behind the bars: every month of an open year, every year boundary.
  const grid = <div className="gantt-grid" style={track} aria-hidden="true">
    {scale.years.map(year => <span key={year.year} className="gantt-grid-year" style={{ left: year.x, width: year.width }}>
      {year.expanded && MONTH_INITIALS.slice(1).map((_, index) => <span key={index} className="gantt-grid-month" style={{ left: (index + 1) * scale.monthWidth }} />)}
    </span>)}
    {todayX !== null && <span className="gantt-today" style={{ left: todayX }} />}
  </div>

  return <div className="gantt" role="group" aria-label={label}>
    <div className="gantt-toolbar">
      <div className="gantt-legend" aria-label="Legend">
        {STAGES.map(stage => <span key={stage} className="gantt-legend-item"><span className={`gantt-swatch is-${stage}`} aria-hidden="true" />{STAGE_LABEL[stage]}</span>)}
        {todayX !== null && <span className="gantt-legend-item"><span className="gantt-swatch is-today" aria-hidden="true" />Today</span>}
        {anyRisk && <span className="gantt-legend-item"><TriangleAlert size={13} className="gantt-risk" aria-hidden="true" />Risk: the crop may fail or yield less</span>}
        {anyWarning && <span className="gantt-legend-item"><CircleAlert size={13} className="gantt-warning" aria-hidden="true" />Warning: missing data or a limit to check</span>}
      </div>
      <button type="button" className="button button-quiet gantt-expand" onClick={() => setOpen(allOpen ? new Set() : new Set(scale.years.map(year => year.year)))}>
        <span>{allOpen ? 'Collapse to years' : 'Expand all months'}</span></button>
    </div>
    <div className="gantt-scroll" ref={scroller}>
      <div className="gantt-sheet" style={{ '--gantt-track': `${scale.width}px` } as React.CSSProperties}>
        <div className="gantt-row gantt-head">
          <div className="gantt-label">Crop</div>
          <div className="gantt-track" style={track}>
            {scale.years.map(year => <button key={year.year} type="button" className={`gantt-year ${year.expanded ? 'is-open' : ''}`} style={{ left: year.x, width: year.width }}
              aria-expanded={year.expanded} aria-label={`${year.year}, ${year.expanded ? 'collapse to the year' : 'show months'}`} onClick={() => toggle(year.year)}>
              {year.expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}<span className="mono">{year.year}</span>
            </button>)}
          </div>
        </div>
        <div className="gantt-row gantt-months" aria-hidden="true">
          <div className="gantt-label" />
          <div className="gantt-track" style={track}>
            {scale.years.filter(year => year.expanded).flatMap(year => MONTH_INITIALS.map((initial, index) =>
              <span key={`${year.year}-${index}`} className="gantt-month" style={{ left: year.x + index * scale.monthWidth, width: scale.monthWidth }}>{initial}</span>))}
          </div>
        </div>
        {groups.map(group => <div key={group.key} className={`gantt-group ${group.muted ? 'is-muted' : ''}`} role="rowgroup" aria-label={group.label}>
          {groups.length > 1 && <div className="gantt-row gantt-group-head"><div className="gantt-label">{group.label}</div><div className="gantt-track" style={track}>{grid}</div></div>}
          {!group.rows.length && <div className="gantt-row"><div className="gantt-label gantt-empty">No crops in this period</div><div className="gantt-track" style={track}>{grid}</div></div>}
          {group.rows.map(row => {
            const crop = <>
              <span className="gantt-crop-dot" style={{ background: cropColour(row.cropId) }} aria-hidden="true" />
              <span className="gantt-crop"><strong>{row.cropName}</strong><span className="gantt-crop-dates">{`${whenText(row.plantingDate, row.plantingMonth)} – ${whenText(row.clearanceDate, row.clearanceMonth)}`}</span></span>
            </>
            return <div key={row.key} className="gantt-row">
              <div className="gantt-label">
                {row.onSelect
                  ? <button type="button" className="gantt-crop-button" onClick={row.onSelect} aria-label={`Edit ${row.cropName}`}>{crop}</button>
                  : crop}
                {/* Each icon explains only its own kind on hover. */}
                {row.risks.length + row.warnings.length > 0 && <span className="gantt-flags">
                {row.risks.length > 0 && <span className="gantt-risk" title={`Risks:\n${row.risks.join('\n')}`} role="img"
                  aria-label={`${plural(row.risks.length, 'risk')}: ${row.risks.join(' · ')}`}><TriangleAlert size={13} aria-hidden="true" /></span>}
                {row.warnings.length > 0 && <span className="gantt-warning" title={`Warnings:\n${row.warnings.join('\n')}`} role="img"
                  aria-label={`${plural(row.warnings.length, 'warning')}: ${row.warnings.join(' · ')}`}><CircleAlert size={13} aria-hidden="true" /></span>}
                </span>}
              </div>
              <div className="gantt-track" style={track}>
                {grid}
                {/* No visible text on the bars, not even a hover tooltip: the colour is the stage. Screen readers still get it. */}
                {cycleStages(row).map(span => {
                  const left = scale.x(span.from), width = Math.max(scale.x(span.to, true) - left, 3)
                  const text = `${row.cropName}: ${STAGE_LABEL[span.stage]}, ${span.from === span.to ? monthText(span.from) : `${monthText(span.from)} – ${monthText(span.to)}`}`
                  return <span key={`${span.stage}-${span.from}`} className={`gantt-bar is-${span.stage}`} style={{ left, width }} role="img" aria-label={text} />
                })}
              </div>
            </div>
          })}
        </div>)}
      </div>
    </div>
  </div>
}
