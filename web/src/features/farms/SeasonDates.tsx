import 'react-day-picker/style.css'
import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { DayPicker, type Matcher } from 'react-day-picker'
import { CalendarDays, ChevronDown, ChevronUp } from 'lucide-react'
import { sowingDayChoices, toIso } from './sowingDays'
import { addMonths, canExtendEarlier, canExtendLater, extendEarlier, extendLater, firstOfMonth, monthsBetween, openingWindow, yearWindow, type MonthWindow } from './calendarWindow'

/** Another crop's period on the same field, ISO dates, inclusive; `to: null` means still growing. */
export type Occupied = { from: string; to: string | null; label: string }

type SeasonDatesProps = {
  id: string; sown: string; harvested: string
  occupied: Occupied[]; maxHarvest: string | null; disabled: boolean; focusHarvest?: boolean
  onChange: (dates: { sown?: string; harvested?: string }) => void
}

/**
 * Sowing and harvest dates for one crop. The calendar is a vertical run of months that opens on the date already
 * chosen, otherwise on this month (harvest on the sowing month when that is later), scrolling smoothly down to it. Days
 * taken by other crops on the field cannot be picked. The year comes from the dates themselves, so it is never typed
 * twice. Any day can be sown on; the 1st, 11th and 21st of the month in view are one click away. Done in its header, a
 * second press on the open date field, Esc or a press outside folds the calendar away.
 */
export function SeasonDates({ id, sown, harvested, occupied, maxHarvest, disabled, focusHarvest, onChange }: SeasonDatesProps) {
  const [open, setOpen] = useState<'sown' | 'harvested' | null>(focusHarvest ? 'harvested' : null)
  // The calendar stays in the page while it folds away, then leaves.
  const [closing, setClosing] = useState(false)
  const closeTimer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const slot = useRef<HTMLDivElement>(null)
  const triggers = { sown: useRef<HTMLButtonElement>(null), harvested: useRef<HTMLButtonElement>(null) }
  const expanded = closing ? null : open

  function fold() {
    if (globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches) { setOpen(null); return }
    setClosing(true)
    closeTimer.current = setTimeout(() => { setOpen(null); setClosing(false) }, CLOSE_MS)
  }
  function collapse(focus = true) {
    if (!expanded) return
    if (focus) triggers[expanded].current?.focus()
    fold()
  }
  function show(which: 'sown' | 'harvested') {
    clearTimeout(closeTimer.current)
    setClosing(false); setOpen(which)
  }
  useEffect(() => () => clearTimeout(closeTimer.current), [])

  // A press anywhere outside the calendar and its two date fields folds it away; the fields toggle it themselves.
  const { sown: sownTrigger, harvested: harvestedTrigger } = triggers
  useEffect(() => {
    if (!expanded) return
    const onPointerDown = (event: MouseEvent) => {
      const target = event.target as Node
      if ([slot, sownTrigger, harvestedTrigger].some(ref => ref.current?.contains(target))) return
      fold()
    }
    document.addEventListener('mousedown', onPointerDown)
    return () => document.removeEventListener('mousedown', onPointerDown)
  }, [expanded])

  const taken: Matcher[] = occupied.map(period => period.to ? { from: toDate(period.from), to: toDate(period.to) } : { after: addDays(toDate(period.from), -1) })
  // Harvest can't run into the next crop sown after this one.
  const nextStart = sown ? occupied.map(period => period.from).filter(from => from > sown).sort()[0] : undefined
  const harvestLimit = earliest(nextStart ? addDays(toDate(nextStart), -1) : undefined, maxHarvest ? toDate(maxHarvest) : undefined)

  const picker = open && (() => {
    const isSown = open === 'sown'
    const value = isSown ? sown : harvested
    const selected = value ? toDate(value) : undefined
    const today = new Date()
    const thisMonth = new Date(today.getFullYear(), today.getMonth(), 1)
    const fallback = isSown ? thisMonth : latest(sown ? toDate(sown) : undefined, thisMonth)!
    // The months span the farm's history up to a long perennial ahead.
    const start = !isSown && sown ? toDate(sown) : new Date(FIRST_YEAR, 0, 1)
    const end = new Date(Math.max(today.getFullYear(), sown ? toDate(sown).getFullYear() : 0) + YEARS_AHEAD, 11, 31)
    const disabledDays: Matcher[] = [...taken,
      ...(isSown && harvested ? [{ after: toDate(harvested) }] : []),
      ...(!isSown && sown ? [{ before: toDate(sown) }] : []),
      ...(!isSown && harvestLimit ? [{ after: harvestLimit }] : [])]
    const close = collapse
    return <VerticalCalendar key={open} label={isSown ? 'Sowing date' : 'Harvest date'} selected={selected} anchor={selected ?? fallback}
      first={earliest(start, selected)!} last={latest(end, selected)!} disabledDays={disabledDays} taken={taken}
      sowingValue={isSown ? value : null} onClose={close}
      onSelect={date => { onChange(isSown ? { sown: date ? toIso(date) : '' } : { harvested: date ? toIso(date) : '' }); if (date) close() }}
      extraFoot={!isSown && harvested && <button type="button" className="text-button" onClick={() => { onChange({ harvested: '' }); close() }}>Still growing</button>} />
  })()

  const trigger = (which: 'sown' | 'harvested', label: string, value: string, empty: string, optional = false) =>
    <div className="crop-form-row">
      <label htmlFor={`${which}-${id}`}>{label}{optional && <span className="optional"> optional</span>}</label>
      <button ref={triggers[which]} id={`${which}-${id}`} type="button" className={`date-trigger ${value ? '' : 'is-empty'}`} disabled={disabled}
        aria-expanded={expanded === which} aria-controls={`calendar-${id}`}
        onClick={() => { if (expanded === which) collapse(false); else show(which) }}
        onKeyDown={event => { if (event.key === 'Escape' && expanded) { event.stopPropagation(); collapse() } }}>
        <CalendarDays size={15} aria-hidden="true" /><span>{value ? formatDate(value) : empty}</span>
      </button>
    </div>

  return <>
    {trigger('sown', 'Sown', sown, 'Pick a date')}
    {trigger('harvested', 'Harvested', harvested, 'Still growing', true)}
    {picker && <div ref={slot} id={`calendar-${id}`} className={`crop-calendar-slot ${closing ? 'is-closing' : ''}`}>{picker}</div>}
  </>
}

const WEEKDAYS = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su']

type VerticalCalendarProps = {
  label: string; selected: Date | undefined; anchor: Date; first: Date; last: Date
  disabledDays: Matcher[]; taken: Matcher[]
  /** The sowing date when this is the sowing calendar, which offers the usual sowing days; null for harvest. */
  sowingValue: string | null
  onSelect: (date: Date | undefined) => void; onClose: (focus?: boolean) => void
  extraFoot?: ReactNode
}

/**
 * Months one under another in a scrolling strip, under one fixed row of weekdays; each month's name stays pinned at
 * the top while its days scroll by. More months load above and below on request, and the year menu jumps anywhere.
 */
export function VerticalCalendar({ label, selected, anchor, first, last, disabledDays, taken, sowingValue, onSelect, onClose, extraFoot }: VerticalCalendarProps) {
  const [range, setRange] = useState<MonthWindow>(() => openingWindow(anchor, first, last))
  const [inView, setInView] = useState(() => firstOfMonth(anchor))
  const scroller = useRef<HTMLDivElement>(null)
  // What to scroll to after the next render: a month to bring to the top (smoothly or not), or a height to keep in place.
  const pending = useRef<{ month: Date; smooth: boolean; focus: boolean } | { keepFrom: number } | null>({ month: firstOfMonth(anchor), smooth: true, focus: true })

  const monthElement = (month: Date) => scroller.current?.querySelectorAll<HTMLElement>('.rdp-month')[monthsBetween(range.start, month)]
  // Where a month starts in the strip, measured below the pinned weekday row.
  const offsetOf = (element: HTMLElement) => element.getBoundingClientRect().top - scroller.current!.getBoundingClientRect().top + scroller.current!.scrollTop
    - (scroller.current!.querySelector<HTMLElement>('.crop-calendar-weekdays')?.offsetHeight ?? 0)

  useLayoutEffect(() => {
    const target = pending.current, box = scroller.current
    pending.current = null
    if (!target || !box) return
    if ('keepFrom' in target) { box.scrollTop += box.scrollHeight - target.keepFrom; return }
    const element = monthElement(target.month)
    if (!element) return
    const reduce = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    box.scrollTo({ top: offsetOf(element), behavior: target.smooth && !reduce ? 'smooth' : 'auto' })
    if (target.focus) {
      // The chosen day, else the first day that can be picked in that month; focused without a jump, so the scroll stays smooth.
      const day = (selected && box.querySelector<HTMLElement>(`[data-day="${toIso(selected)}"] button:not(:disabled)`))
        || element.querySelector<HTMLElement>('.rdp-day button:not(:disabled)')
      day?.focus({ preventScroll: true })
    }
  })

  /** The month at the top of the strip, for the sowing-day shortcuts. */
  function onScroll() {
    const box = scroller.current
    if (!box) return
    const months = [...box.querySelectorAll<HTMLElement>('.rdp-month')]
    const top = box.scrollTop + 24
    let index = 0
    months.forEach((element, at) => { if (offsetOf(element) <= top) index = at })
    const month = addMonths(range.start, index)
    if (monthsBetween(inView, month) !== 0) setInView(month)
  }

  function earlier() {
    pending.current = { keepFrom: scroller.current?.scrollHeight ?? 0 }
    setRange(extendEarlier(range, first))
  }
  function jumpToYear(year: number) {
    const next = yearWindow(year, first, last)
    setRange(next); setInView(next.start)
    pending.current = { month: next.start, smooth: false, focus: false }
  }

  const sowingDays = sowingValue === null ? [] : sowingDayChoices(inView, disabledDays)
  const years = Array.from({ length: last.getFullYear() - first.getFullYear() + 1 }, (_, index) => first.getFullYear() + index)

  return <div className="crop-calendar is-vertical" role="group" aria-label={label}
    onKeyDown={(event: KeyboardEvent) => { if (event.key === 'Escape') { event.stopPropagation(); onClose() } }}>
    <div className="crop-calendar-bar">
      <label className="crop-calendar-year"><span className="visually-hidden">Jump to year</span>
        <select value={inView.getFullYear()} onChange={event => jumpToYear(Number(event.target.value))}>
          {years.map(year => <option key={year} value={year}>{year}</option>)}</select></label>
      <button type="button" className="crop-calendar-collapse" title="Collapse the calendar" onClick={() => onClose()}>
        Done<ChevronUp size={14} aria-hidden="true" /></button>
    </div>
    <div ref={scroller} className="crop-calendar-scroll" onScroll={onScroll}>
      {/* Inside the strip, so its columns line up with the days whatever the scrollbar takes. */}
      <div className="crop-calendar-weekdays" aria-hidden="true">{WEEKDAYS.map(day => <span key={day}>{day}</span>)}</div>
      {canExtendEarlier(range, first) && <button type="button" className="crop-calendar-more" onClick={earlier}>
        <ChevronUp size={14} aria-hidden="true" />Earlier months</button>}
      <DayPicker mode="single" selected={selected} month={range.start} numberOfMonths={range.count} onMonthChange={month => setRange({ ...range, start: firstOfMonth(month) })}
        startMonth={first} endMonth={last} hideNavigation hideWeekdays weekStartsOn={1}
        disabled={disabledDays} modifiers={{ taken }} modifiersClassNames={{ taken: 'is-taken' }} onSelect={onSelect} />
      {canExtendLater(range, last) && <button type="button" className="crop-calendar-more" onClick={() => setRange(extendLater(range, last))}>
        <ChevronDown size={14} aria-hidden="true" />Later months</button>}
    </div>
    {sowingValue !== null && <div className="crop-calendar-days" role="group" aria-label="Usual sowing days">
      <span>Sow on the</span>
      {sowingDays.map(day => <button key={day.iso} type="button" className={`text-button ${sowingValue === day.iso ? 'is-selected' : ''}`}
        disabled={day.disabled} aria-label={`Sow on ${formatDate(day.iso)}`}
        onClick={() => { onSelect(toDate(day.iso)) }}>{day.label}</button>)}
      <span className="crop-calendar-days-month">of {MONTH_YEAR.format(inView)}</span>
    </div>}
    {(taken.length > 0 || extraFoot) && <div className="crop-calendar-foot">
      {taken.length > 0 && <span className="crop-calendar-legend"><i />Taken by another crop</span>}
      {extraFoot}
    </div>}
  </div>
}

const FIRST_YEAR = 1950, YEARS_AHEAD = 30
/** How long the calendar takes to fold away; matches calendar-close in map.css. */
const CLOSE_MS = 180
const MONTH_YEAR = new Intl.DateTimeFormat('en', { month: 'short', year: 'numeric' })
const toDate = (iso: string) => { const [y, m, d] = iso.split('-').map(Number); return new Date(y, m - 1, d) }
const addDays = (date: Date, days: number) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)
const defined = (dates: (Date | undefined)[]) => dates.filter((date): date is Date => date !== undefined)
const earliest = (...dates: (Date | undefined)[]) => defined(dates).sort((a, b) => +a - +b)[0]
const latest = (...dates: (Date | undefined)[]) => defined(dates).sort((a, b) => +b - +a)[0]
const formatDate = (iso: string) => new Intl.DateTimeFormat('en', { day: 'numeric', month: 'short', year: 'numeric' }).format(toDate(iso))
