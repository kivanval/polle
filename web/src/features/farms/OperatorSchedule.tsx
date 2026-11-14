import { useEffect, useMemo, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import { Check, Copy, Eraser, GitBranch, Moon, Plus, Sun, Tractor, Undo2, Users } from 'lucide-react'
import { problemMessage } from './api'
import { useOperatorShifts, useSaveOperatorSchedule, type OperatorShift } from './operatorShifts'
import { useLaborCategories, type LaborCategory } from './resources'
import {
  addShift, copyRowToAll, DAYS, dayHours, emptyGrid, gridsEqual, gridToShifts, HOURS, inRect, paintRect, PRESETS, setColumn, setRow, shiftsToGrid, shiftStatus,
  weekHours, weekRuns, type Cell, type Grid, type Preset, type ShiftLike,
} from './scheduleGrid'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'

const fmt = (options: Intl.DateTimeFormatOptions, index: number) => new Intl.DateTimeFormat('en', { ...options, timeZone: 'UTC' }).format(Date.UTC(2026, index, 1))
const MONTHS = Array.from({ length: 12 }, (_, index) => fmt({ month: 'long' }, index))
const MONTHS_SHORT = Array.from({ length: 12 }, (_, index) => fmt({ month: 'short' }, index))
const WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
const WEEKDAYS_SHORT = WEEKDAYS.map(day => day.slice(0, 3))
const clock = (hour: number) => (hour >= HOURS ? 'midnight' : `${String(hour).padStart(2, '0')}:00`)
const cellKey = (day: number, hour: number) => `${day}-${hour}`
const tone = (hour: number) => (hour < 5 || hour >= 20 ? 'night' : hour < 8 ? 'dawn' : hour < 17 ? 'day' : 'dusk')
const without = (drafts: Record<number, Grid>, months: number[]) =>
  Object.fromEntries(Object.entries(drafts).filter(([month]) => !months.includes(Number(month)))) as Record<number, Grid>

type Drag = { anchor: Cell; current: Cell; brush: boolean }
type Edit = { month: number; before: Grid | undefined }
type Pop = { id: number; origin: Cell; cells: Set<string> }

type Labor = number | null
const ALL_MONTHS = Array.from({ length: 12 }, (_, index) => index + 1)

/**
 * Working hours by labour type. The farm default week applies to every labour type without a week of its own;
 * a type can take its own week (copied from the default to start) or go back to following the default.
 */
export function OperatorSchedule({ farmId }: { farmId: string }) {
  const shifts = useOperatorShifts(farmId)
  const categories = useLaborCategories()
  const save = useSaveOperatorSchedule(farmId)
  const [labor, setLabor] = useState<Labor>(null)
  const [dirty, setDirty] = useState(false)
  const [error, setError] = useState('')
  // Types given their own week while the default is empty: nothing to copy, so they open as an empty editable week
  // and exist on the server once saved.
  const [claimed, setClaimed] = useState<number[]>([])
  const [confirmFollow, setConfirmFollow] = useState(false)
  if (shifts.isPending || categories.isPending) return <Frame><div className="schedule-state" role="status"><TractorLoader compact />Loading working hours…</div></Frame>
  if (shifts.error || categories.error) return <Frame><div className="schedule-state"><p role="alert">{problemMessage(shifts.error ?? categories.error)}</p>
    <button className="button button-quiet" onClick={() => { void shifts.refetch(); void categories.refetch() }}>Try again</button></div></Frame>

  const own = (id: Labor) => shifts.data.filter(shift => shift.laborCategoryId === id)
  const hasOwn = (id: number) => shifts.data.some(shift => shift.laborCategoryId === id)
  const current = categories.data.find(category => category.id === labor) ?? null
  const inherited = labor !== null && !hasOwn(labor) && !claimed.includes(labor)
  const defaults = own(null)
  const label = (category: LaborCategory | null) => category?.name ?? 'Farm default'

  /** Copies a year of weeks to a labour type, one request per group of months that share a week. */
  async function copyYear(target: number, source: OperatorShift[]) {
    setError('')
    if (source.length === 0) { setClaimed(current => [...current, target]); return }
    const groups = new Map<string, { months: number[]; shifts: ReturnType<typeof gridToShifts> }>()
    for (const month of ALL_MONTHS) {
      const week = gridToShifts(shiftsToGrid(source.filter(shift => shift.month === month)).grid)
      const id = JSON.stringify(week)
      groups.set(id, { months: [...(groups.get(id)?.months ?? []), month], shifts: week })
    }
    try {
      for (const group of groups.values()) await save.mutateAsync({ laborCategoryId: target, months: group.months, shifts: group.shifts })
    } catch (reason) { setError(problemMessage(reason, 'shifts')) }
  }

  async function followDefault(target: number) {
    setError('')
    try {
      await save.mutateAsync({ laborCategoryId: target, months: ALL_MONTHS, shifts: [] })
      setClaimed(current => current.filter(id => id !== target)); setConfirmFollow(false)
    } catch (reason) { setError(problemMessage(reason, 'shifts')) }
  }

  return <Frame>
    <div className="labor-tabs" role="group" aria-label="Labour type">
      {[null, ...categories.data].map(category => {
        const id = category?.id ?? null
        const ownWeek = id === null ? defaults.length > 0 : hasOwn(id)
        const locked = dirty && labor !== id
        return <button key={id ?? 'default'} type="button" aria-pressed={labor === id} disabled={save.isPending || locked}
          title={locked ? 'Save or undo your changes first' : undefined}
          className={`labor-tab ${id === null ? 'is-default' : ''} ${ownWeek ? 'has-own' : ''}`} onClick={() => { setLabor(id); setError(''); setConfirmFollow(false) }}>
          <span className="labor-tab-name">{id === null && <Users size={13} />}{label(category)}</span>
          <small>{id === null ? (ownWeek ? 'Applies to every other type' : 'No hours yet') : ownWeek ? 'Own hours' : 'Follows default'}</small>
        </button>
      })}
    </div>
    {categories.data.length === 0 && <p className="schedule-rounded">No labour types are listed in Expert data yet, so only the farm default week can be set.</p>}

    {labor !== null && (inherited
      ? <div className="labor-inherit">
          <p><GitBranch size={15} aria-hidden="true" /><span><strong>{label(current)}</strong> follows the farm default{defaults.length ? ', shown below' : ', which has no hours yet'}.
            Give this type its own hours to change them without affecting the others.</span></p>
          <button type="button" className="button button-dark" disabled={save.isPending} onClick={() => void copyYear(labor, defaults)}>
            {save.isPending ? <TractorSpinner size={15} /> : <Plus size={15} />}Give {label(current)} its own hours</button>
        </div>
      : confirmFollow
        ? <div className="labor-inherit is-own" role="alertdialog" aria-label={`Remove ${label(current)}’s own hours?`}>
            <p><span>Remove <strong>{label(current)}</strong>’s own hours for every month? It will follow the farm default again.</span></p>
            <div className="field-actions">
              <button type="button" className="button button-danger" disabled={save.isPending} onClick={() => void followDefault(labor)}>
                {save.isPending && <TractorSpinner size={15} />}Remove own hours</button>
              <button type="button" className="button button-quiet" autoFocus disabled={save.isPending} onClick={() => setConfirmFollow(false)}>Keep them</button>
            </div>
          </div>
        : <div className="labor-inherit is-own">
            <p><span><strong>{label(current)}</strong> has its own hours, so the farm default does not apply to it.</span></p>
            <button type="button" className="button button-quiet" disabled={save.isPending || dirty}
              onClick={() => hasOwn(labor) ? setConfirmFollow(true) : setClaimed(current => current.filter(id => id !== labor))}>Follow the farm default instead</button>
          </div>)}
    {error && <p className="field-error" role="alert">{error}</p>}

    <WeekEditor key={labor ?? 'default'} label={label(current)} shifts={inherited ? defaults : own(labor)} readOnly={inherited} onDirtyChange={setDirty}
      saving={save.isPending} onSave={async (months, week) => { await save.mutateAsync({ laborCategoryId: labor, months, shifts: week }) }} />
  </Frame>
}

/** A month-by-month week of shifts. Saving writes the shown week to the chosen months through `onSave`. */
export function WeekEditor({ label, shifts: list, readOnly, onDirtyChange, saving, onSave }: {
  label: string; shifts: (ShiftLike & { month: number })[]; readOnly: boolean; onDirtyChange: (dirty: boolean) => void
  saving: boolean; onSave: (months: number[], shifts: ReturnType<typeof gridToShifts>) => Promise<void>
}) {
  const [month, setMonth] = useState(() => new Date().getMonth() + 1)
  const [drafts, setDrafts] = useState<Record<number, Grid>>({})
  const [history, setHistory] = useState<Edit[]>([])
  const [drag, setDrag] = useState<Drag | null>(null)
  const [preview, setPreview] = useState<Grid | null>(null)
  const [pop, setPop] = useState<Pop | null>(null)
  const [also, setAlso] = useState<number[]>([])
  const [focus, setFocus] = useState<Cell>({ day: 0, hour: 8 })
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [now, setNow] = useState(() => new Date())
  const matrix = useRef<HTMLDivElement>(null)
  const refocus = useRef(false)
  const popTimer = useRef<number | undefined>(undefined)

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000)
    return () => { window.clearInterval(timer); window.clearTimeout(popTimer.current) }
  }, [])
  useEffect(() => {
    if (!refocus.current) return
    refocus.current = false
    matrix.current?.querySelector<HTMLElement>(`[data-day="${focus.day}"][data-hour="${focus.hour}"]`)?.focus()
  }, [focus])

  const base = useMemo(() => {
    const byMonth = new Map<number, { dayOfWeek: number; startTime: string; endTime: string }[]>()
    for (const shift of list) byMonth.set(shift.month, [...(byMonth.get(shift.month) ?? []), shift])
    return Object.fromEntries(Array.from({ length: 12 }, (_, index) => {
      const list = byMonth.get(index + 1) ?? []
      return [index + 1, { ...shiftsToGrid(list), count: list.length }]
    })) as Record<number, { grid: Grid; rounded: number; count: number }>
  }, [list])
  const anyDraft = Object.keys(drafts).length > 0
  useEffect(() => { onDirtyChange(anyDraft) }, [anyDraft, onDirtyChange])

  const grid = drafts[month] ?? base[month]?.grid ?? emptyGrid()
  const dirty = Boolean(drafts[month])
  const busy = saving || readOnly
  const hours = weekHours(grid)
  const daysWorked = grid.filter(row => dayHours(row) > 0).length
  const isThisMonth = now.getMonth() + 1 === month
  const today = (now.getDay() + 6) % 7
  const status = isThisMonth ? shiftStatus(grid, today, now.getHours()) : null
  const months = [month, ...also.filter(other => other !== month)].sort((a, b) => a - b)

  function commit(next: Grid, origin: Cell) {
    if (gridsEqual(next, grid)) return
    const changed = new Set<string>()
    next.forEach((row, day) => row.forEach((on, hour) => { if (on !== grid[day][hour]) changed.add(cellKey(day, hour)) }))
    setHistory(entries => [...entries.slice(-49), { month, before: drafts[month] }])
    setDrafts(current => (gridsEqual(next, base[month].grid) ? without(current, [month]) : { ...current, [month]: next }))
    setPop(current => ({ id: (current?.id ?? 0) + 1, origin, cells: changed }))
    window.clearTimeout(popTimer.current)
    popTimer.current = window.setTimeout(() => setPop(null), 900)
    setNotice(''); setError('')
  }

  function undo() {
    const last = history.at(-1)
    if (!last) return
    setHistory(entries => entries.slice(0, -1))
    setMonth(last.month)
    setDrafts(current => (last.before ? { ...current, [last.month]: last.before } : without(current, [last.month])))
    setNotice(''); setError('')
  }

  function cellAt(x: number, y: number): Cell | null {
    const element = document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-day]')
    return element ? { day: Number(element.dataset.day), hour: Number(element.dataset.hour) } : null
  }
  function onPointerDown(event: PointerEvent<HTMLDivElement>) {
    if (busy || event.button !== 0) return
    const cell = cellAt(event.clientX, event.clientY)
    if (!cell) return
    event.currentTarget.setPointerCapture(event.pointerId)
    setFocus(cell)
    setDrag({ anchor: cell, current: cell, brush: !grid[cell.day][cell.hour] })
  }
  function onPointerMove(event: PointerEvent<HTMLDivElement>) {
    if (!drag) return
    const cell = cellAt(event.clientX, event.clientY)
    if (cell && (cell.day !== drag.current.day || cell.hour !== drag.current.hour)) setDrag({ ...drag, current: cell })
  }
  function onPointerUp(event: PointerEvent<HTMLDivElement>) {
    if (!drag) return
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
    commit(paintRect(grid, drag.anchor, drag.current, drag.brush), drag.anchor)
    setDrag(null)
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const element = (event.target as HTMLElement).closest<HTMLElement>('[data-day]')
    if (!element || busy) return
    const cell = { day: Number(element.dataset.day), hour: Number(element.dataset.hour) }
    if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault()
      commit(paintRect(grid, cell, cell, !grid[cell.day][cell.hour]), cell)
      return
    }
    const moves: Record<string, Cell> = {
      ArrowRight: { day: cell.day, hour: Math.min(HOURS - 1, cell.hour + 1) }, ArrowLeft: { day: cell.day, hour: Math.max(0, cell.hour - 1) },
      ArrowDown: { day: Math.min(DAYS - 1, cell.day + 1), hour: cell.hour }, ArrowUp: { day: Math.max(0, cell.day - 1), hour: cell.hour },
      Home: { day: cell.day, hour: 0 }, End: { day: cell.day, hour: HOURS - 1 },
    }
    if (!moves[event.key]) return
    event.preventDefault()
    refocus.current = true
    setFocus(moves[event.key])
  }

  function applyPreset(preset: Preset) {
    setPreview(null)
    commit(preset.build(), { day: 3, hour: 0 })
  }

  async function saveSchedule() {
    setError(''); setNotice('')
    try {
      await onSave(months, gridToShifts(grid))
      setDrafts(current => without(current, months))
      setHistory(entries => entries.filter(entry => !months.includes(entry.month)))
      setAlso([])
      setNotice(`Saved for ${months.map(value => MONTHS[value - 1]).join(', ')}.`)
    } catch (reason) { setError(problemMessage(reason, 'shifts')) }
  }

  const rounded = base[month].rounded
  const everyMonth = also.length === 11
  const ready = dirty || also.length > 0

  return <div className={`week-editor ${readOnly ? 'is-read-only' : ''}`}>
    <div role="group" aria-label="Month" className="month-strip">
      {MONTHS.map((name, index) => {
        const value = index + 1
        return <button key={name} type="button" aria-pressed={value === month} aria-label={name} disabled={saving}
          className={`month-chip ${value === now.getMonth() + 1 ? 'is-current' : ''} ${base[value].count > 0 ? 'has-shifts' : ''} ${drafts[value] ? 'is-dirty' : ''}`}
          onClick={() => { setMonth(value); setPreview(null); setNotice(''); setError('') }}>{MONTHS_SHORT[index]}</button>
      })}
    </div>

    <div className="schedule-live" role="status">
      {status
        ? status.onShift
          ? <><span className="live-dot is-on" />On shift now, until {clock(status.endsAt)}</>
          : <><span className="live-dot" />Off shift{status.next ? `, next: ${WEEKDAYS_SHORT[status.next.day]} ${clock(status.next.hour)}` : ' — no shifts planned this month'}</>
        : <span className="schedule-live-quiet">{MONTHS[month - 1]} repeats every year. Every month keeps its own week.</span>}
    </div>

    {!readOnly && <div className="schedule-tools">
      <div className="schedule-presets" role="group" aria-label="Quick starts">
        <span className="schedule-tools-label">Quick starts</span>
        {PRESETS.map(preset => <button key={preset.id} type="button" className="preset" disabled={busy} onClick={() => applyPreset(preset)}
          onMouseEnter={() => setPreview(preset.build())} onMouseLeave={() => setPreview(null)}
          onFocus={() => setPreview(preset.build())} onBlur={() => setPreview(null)}><strong>{preset.name}</strong><small>{preset.detail}</small></button>)}
      </div>
      <div className="schedule-edit-actions">
        <button type="button" className="button button-quiet" disabled={busy || history.length === 0} onClick={undo} title="Undo (Ctrl+Z)"><Undo2 size={15} />Undo</button>
        <button type="button" className="button button-quiet" disabled={busy || hours === 0}
          onClick={() => commit(emptyGrid(), { day: 3, hour: 12 })}><Eraser size={15} />Clear week</button>
      </div>
      <QuickShift disabled={busy} onAdd={(days, from, to) => commit(addShift(grid, days, from, to), { day: days[0], hour: from })} />
    </div>}

    {rounded > 0 && <p className="schedule-rounded" role="status">{rounded === 1 ? 'One shift' : `${rounded} shifts`} in {MONTHS[month - 1]} use minutes. The grid works in whole hours,
      so {rounded === 1 ? 'it is' : 'they are'} shown rounded, and saving stores whole hours.</p>}

    <div className="schedule-scroll" onKeyDown={event => {
      if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === 'z' && !(event.target instanceof HTMLInputElement)) { event.preventDefault(); undo() }
    }}>
      <div ref={matrix} className={`schedule-matrix ${drag ? 'is-dragging' : ''}`} role="grid" aria-label={`${label} working hours, ${MONTHS[month - 1]}`}
        aria-describedby="schedule-help" aria-busy={busy} onPointerDown={onPointerDown} onPointerMove={onPointerMove} onPointerUp={onPointerUp}
        onPointerCancel={() => setDrag(null)} onKeyDown={onKeyDown}>
        <div className="matrix-skyline" aria-hidden="true"><Moon size={13} className="sky-moon" /><Sun size={15} className="sky-sun" /><Moon size={13} className="sky-moon-late" /></div>
        <div className="matrix-row matrix-head" role="row">
          <span className="matrix-corner" role="columnheader"><span className="visually-hidden">Day</span></span>
          {Array.from({ length: HOURS }, (_, hour) => <div key={hour} role="columnheader" className="matrix-hour">
            <button type="button" disabled={busy} aria-label={`Toggle ${clock(hour)} on every day`} onClick={() => commit(setColumn(grid, hour, !grid.every(row => row[hour])), { day: 3, hour })}>
              {hour % 3 === 0 ? String(hour).padStart(2, '0') : ''}</button></div>)}
          <span className="matrix-total-head" role="columnheader">Hours</span><span role="columnheader" />
        </div>
        {WEEKDAYS.map((name, day) => {
          const shownRow = Array.from({ length: HOURS }, (_, hour) => (drag && inRect({ day, hour }, drag.anchor, drag.current) ? drag.brush : grid[day][hour]))
          return <div key={name} role="row" className={`matrix-row ${isThisMonth && day === today ? 'is-today' : ''}`}>
            <div role="rowheader"><button type="button" className="matrix-day" disabled={busy} aria-label={`${grid[day].every(Boolean) ? 'Clear' : 'Fill'} ${name}`}
              onClick={() => commit(setRow(grid, day, !grid[day].every(Boolean)), { day, hour: 0 })}>{WEEKDAYS_SHORT[day]}</button></div>
            {shownRow.map((on, hour) => {
              const ghost = preview && !drag && preview[day][hour] !== grid[day][hour] ? (preview[day][hour] ? 'on' : 'off') : null
              const wave = pop && pop.cells.has(cellKey(day, hour)) ? pop : null
              const style = wave ? ({ '--wave': Math.abs(day - wave.origin.day) + Math.abs(hour - wave.origin.hour) } as CSSProperties) : undefined
              return <div key={hour} role="gridcell" data-day={day} data-hour={hour} aria-selected={on} style={style}
                aria-label={`${name} ${clock(hour)}, ${on ? 'on shift' : 'off'}`} tabIndex={focus.day === day && focus.hour === hour ? 0 : -1}
                className={['matrix-cell', `tone-${tone(hour)}`, on && 'is-on', Math.floor(hour / 3) % 2 === 1 && 'is-band', ghost && `is-ghost-${ghost}`,
                  isThisMonth && day === today && hour === now.getHours() && 'is-now', wave && `is-pop-${wave.id % 2}`].filter(Boolean).join(' ')} />
            })}
            <span className="matrix-day-total">{shownRow.filter(Boolean).length} h</span>
            <button type="button" className="matrix-copy" disabled={busy} aria-label={`Copy ${name} to every day`} title={`Copy ${name} to every day`}
              onClick={() => commit(copyRowToAll(grid, day), { day, hour: 12 })}><Copy size={13} /></button>
          </div>
        })}
      </div>
    </div>
    <WeekRuns grid={grid} />
    <p id="schedule-help" className="schedule-help">Drag across the grid to paint a block, even over several days. Start on a filled cell to erase. Click a day or an hour to fill its row or column.
      With the keyboard, arrows move and space toggles.</p>

    {!readOnly && <div className="schedule-foot">
      <div className="week-gauge">
        <p className="week-gauge-figures" aria-live="polite"><strong key={hours} className="bump">{hours}</strong> h a week<span>{daysWorked} of 7 days</span></p>
        <div className="week-gauge-track" role="img" aria-label={`${hours} of 168 hours in the week`}>
          <span className="week-gauge-fill" style={{ width: `${(hours / 168) * 100}%` }} />
          <span className="week-gauge-tractor" style={{ left: `${(hours / 168) * 100}%` }}><Tractor size={17} /></span>
        </div>
      </div>
      <div className="schedule-apply">
        <div className="schedule-apply-head"><span>Also use this week in</span>
          <button type="button" className="text-button" disabled={busy}
            onClick={() => setAlso(everyMonth ? [] : Array.from({ length: 12 }, (_, index) => index + 1).filter(value => value !== month))}>{everyMonth ? 'Clear' : 'Every month'}</button></div>
        <div className="schedule-apply-months">
          {MONTHS_SHORT.map((name, index) => index + 1 !== month && <button key={name} type="button" aria-pressed={also.includes(index + 1)} aria-label={MONTHS[index]} disabled={busy}
            className="month-chip is-small" onClick={() => setAlso(current => (current.includes(index + 1) ? current.filter(value => value !== index + 1) : [...current, index + 1]))}>{name}</button>)}
        </div>
      </div>
      <div className="schedule-save">
        <button type="button" className="button button-dark" disabled={busy || !ready} onClick={() => void saveSchedule()}>
          {busy ? <TractorSpinner size={16} /> : <Check size={16} />}{months.length > 1 ? `Save for ${months.length} months` : 'Save schedule'}</button>
        {notice && <p className="schedule-notice" role="status"><Tractor size={14} />{notice}</p>}
        {error && <p className="field-error" role="alert">{error}</p>}
      </div>
    </div>}
  </div>
}

const pad = (hour: number) => `${String(hour).padStart(2, '0')}:00`

/** The week's shifts in words, with night shifts joined across midnight so 22:00–06:00 reads as one shift. */
function WeekRuns({ grid }: { grid: Grid }) {
  const runs = weekRuns(grid)
  if (runs.length === 0) return <p className="week-runs is-empty">No working hours in this week.</p>
  if (runs.length === 1 && runs[0].hours === DAYS * HOURS) return <p className="week-runs is-empty">Around the clock, every day.</p>
  return <ul className="week-runs" aria-label="Shifts this week">
    {runs.slice(0, 14).map(run => <li key={`${run.start.day}-${run.start.hour}`} className={run.overnight ? 'is-overnight' : ''}>
      <strong>{WEEKDAYS_SHORT[run.start.day]}</strong> {pad(run.start.hour)}–{run.overnight && run.hours <= HOURS
        ? <>{WEEKDAYS_SHORT[run.end.day]} {pad(run.end.hour)}</> : run.end.hour === 0 ? '24:00' : pad(run.end.hour)}
      {run.overnight && run.hours < HOURS && <span className="run-badge"><Moon size={11} />overnight</span>}
      {run.hours > HOURS && <span className="run-badge">until {WEEKDAYS_SHORT[run.end.day]} {pad(run.end.hour)}</span>}
      <small>{run.hours} h</small></li>)}
    {runs.length > 14 && <li className="week-runs-more">+{runs.length - 14} more</li>}
  </ul>
}

/** Adds a shift by start and end time; an end at or before the start finishes the next morning. */
function QuickShift({ disabled, onAdd }: { disabled: boolean; onAdd: (days: number[], from: number, to: number) => void }) {
  const [days, setDays] = useState<number[]>([0, 1, 2, 3, 4])
  const [from, setFrom] = useState(8)
  const [to, setTo] = useState(17)
  const overnight = to <= from
  function submit(event: FormEvent) {
    event.preventDefault()
    if (days.length && to !== from) onAdd([...days].sort(), from, to)
  }
  return <form className="quick-shift" onSubmit={submit} aria-label="Add a shift by time">
    <span className="schedule-tools-label">Add a shift</span>
    <div className="quick-shift-days" role="group" aria-label="Days">
      {WEEKDAYS_SHORT.map((name, day) => <button key={name} type="button" aria-pressed={days.includes(day)} disabled={disabled}
        onClick={() => setDays(current => current.includes(day) ? current.filter(value => value !== day) : [...current, day])}>{name.slice(0, 2)}</button>)}
    </div>
    <label><span className="visually-hidden">Start</span>
      <select value={from} disabled={disabled} onChange={event => setFrom(Number(event.target.value))}>
        {Array.from({ length: HOURS }, (_, hour) => <option key={hour} value={hour}>{pad(hour)}</option>)}</select></label>
    <span aria-hidden="true">to</span>
    <label><span className="visually-hidden">End</span>
      <select value={to} disabled={disabled} onChange={event => setTo(Number(event.target.value))}>
        {Array.from({ length: HOURS }, (_, index) => index + 1).map(hour => <option key={hour} value={hour}>{hour === HOURS ? '24:00' : pad(hour)}</option>)}</select></label>
    <span className={`quick-shift-note ${overnight ? 'is-overnight' : ''}`} aria-live="polite">
      {to === from ? 'Choose different times' : overnight ? <><Moon size={12} />ends next day</> : `${to - from} h`}</span>
    <button type="submit" className="button button-quiet" disabled={disabled || !days.length || to === from}><Plus size={14} />Add</button>
  </form>
}

function Frame({ children }: { children: ReactNode }) {
  return <section className="resource-section operator-schedule" aria-labelledby="operator-schedule-title">
    <header className="resource-head"><div><h2 id="operator-schedule-title">Operator working hours</h2>
      <p>Set when each kind of work usually happens. The farm default applies to every labour type without hours of its own. Every month keeps its own week.
        These are time windows: they do not say how many people are available, and the planner does not yet use time of day.
        They are not machine hours, which belong to each machinery line.</p></div></header>
    {children}
  </section>
}
