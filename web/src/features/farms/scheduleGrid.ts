// Pure helpers for the operator schedule matrix: seven weekday rows by 24 hourly cells, one week template per month.
// No imports on purpose, so this file can be exercised directly with `node` (see docs/agricultural-api.md).

export const DAYS = 7
export const HOURS = 24
/** Cell i of day d covers hour i. Monday is row 0, like ISO weekday 1. */
export type Grid = boolean[][]
export type Cell = { day: number; hour: number }
export type ShiftLike = { dayOfWeek: number; startTime: string; endTime: string }

export const emptyGrid = (): Grid => Array.from({ length: DAYS }, () => Array<boolean>(HOURS).fill(false))
export const cloneGrid = (grid: Grid): Grid => grid.map(row => [...row])
export const gridsEqual = (a: Grid, b: Grid) => a.every((row, day) => row.every((on, hour) => on === b[day][hour]))

const minutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
// The API needs an end later than the start on the same day, so the end of the day is stored as 23:59.
const END_OF_DAY = 23 * 60 + 59
const clock = (hour: number) => `${String(hour).padStart(2, '0')}:00`

/** Rounds shifts to whole hours. `rounded` counts shifts that had minutes (or were too short to show) and so change on save. */
export function shiftsToGrid(shifts: ShiftLike[]): { grid: Grid; rounded: number } {
  const grid = emptyGrid()
  let rounded = 0
  for (const shift of shifts) {
    const start = minutes(shift.startTime), end = minutes(shift.endTime)
    const from = Math.round(start / 60)
    const to = end >= END_OF_DAY ? HOURS : Math.round(end / 60)
    const exact = start % 60 === 0 && (end % 60 === 0 || end >= END_OF_DAY)
    if (!exact || to <= from) rounded++
    for (let hour = from; hour < to; hour++) grid[shift.dayOfWeek - 1][hour] = true
  }
  return { grid, rounded }
}

/** Merges neighbouring hours into the fewest shifts. */
export function gridToShifts(grid: Grid): { dayOfWeek: number; startTime: string; endTime: string }[] {
  const shifts: { dayOfWeek: number; startTime: string; endTime: string }[] = []
  grid.forEach((row, day) => {
    let start = -1
    for (let hour = 0; hour <= HOURS; hour++) {
      const on = hour < HOURS && row[hour]
      if (on && start < 0) start = hour
      if (!on && start >= 0) {
        shifts.push({ dayOfWeek: day + 1, startTime: clock(start), endTime: hour === HOURS ? '23:59' : clock(hour) })
        start = -1
      }
    }
  })
  return shifts
}

export const dayHours = (row: boolean[]) => row.filter(Boolean).length
export const weekHours = (grid: Grid) => grid.reduce((sum, row) => sum + dayHours(row), 0)

export const inRect = (cell: Cell, a: Cell, b: Cell) =>
  cell.day >= Math.min(a.day, b.day) && cell.day <= Math.max(a.day, b.day) && cell.hour >= Math.min(a.hour, b.hour) && cell.hour <= Math.max(a.hour, b.hour)

export function paintRect(grid: Grid, a: Cell, b: Cell, value: boolean): Grid {
  return grid.map((row, day) => row.map((on, hour) => (inRect({ day, hour }, a, b) ? value : on)))
}

export const setRow = (grid: Grid, day: number, value: boolean): Grid => grid.map((row, index) => (index === day ? Array<boolean>(HOURS).fill(value) : row))
export const setColumn = (grid: Grid, hour: number, value: boolean): Grid => grid.map(row => row.map((on, index) => (index === hour ? value : on)))
export const copyRowToAll = (grid: Grid, day: number): Grid => grid.map(() => [...grid[day]])

/** Where a run of work ends if the cell is on shift, else where the next one starts (looking a week ahead). */
export function shiftStatus(grid: Grid, day: number, hour: number): { onShift: true; endsAt: number } | { onShift: false; next: Cell | null } {
  if (grid[day][hour]) {
    let end = hour
    while (end < HOURS && grid[day][end]) end++
    return { onShift: true, endsAt: end }
  }
  for (let step = 0; step < DAYS * HOURS; step++) {
    const index = hour + step
    const d = (day + Math.floor(index / HOURS)) % DAYS
    const h = index % HOURS
    if (grid[d][h]) return { onShift: false, next: { day: d, hour: h } }
  }
  return { onShift: false, next: null }
}

const fill = (days: number[], from: number, to: number): Grid => {
  const grid = emptyGrid()
  for (const day of days) for (let hour = from; hour < to; hour++) grid[day][hour] = true
  return grid
}
const nightOwl = (): Grid => {
  const grid = emptyGrid()
  for (let day = 0; day < DAYS; day++) for (const hour of [0, 1, 2, 3, 20, 21, 22, 23]) grid[day][hour] = true
  return grid
}

export type Preset = { id: string; name: string; detail: string; build: () => Grid }
const ALL = [0, 1, 2, 3, 4, 5, 6]
export const PRESETS: Preset[] = [
  { id: 'farm-week', name: 'Farm week', detail: 'Mon–Sat, 06–18', build: () => fill([0, 1, 2, 3, 4, 5], 6, 18) },
  { id: 'early-bird', name: 'Early bird', detail: 'Mon–Fri, 05–13', build: () => fill([0, 1, 2, 3, 4], 5, 13) },
  { id: 'harvest-sprint', name: 'Harvest sprint', detail: 'every day, 05–23', build: () => fill(ALL, 5, 23) },
  { id: 'night-owl', name: 'Night owl', detail: 'every day, 20–04', build: nightOwl },
]
/** A continuous stretch of work in the week. `end` is exclusive; a stretch past midnight is overnight. */
export type Run = { start: Cell; end: Cell; hours: number; overnight: boolean }

/**
 * The week's work as continuous stretches, joined across midnight (and from Sunday into Monday), so a night shift
 * painted as Mon 22–24 plus Tue 00–06 reads as one shift, Mon 22:00 to Tue 06:00.
 */
export function weekRuns(grid: Grid): Run[] {
  const total = DAYS * HOURS
  const on = (index: number) => {
    const wrapped = ((index % total) + total) % total
    return grid[Math.floor(wrapped / HOURS)][wrapped % HOURS]
  }
  const filled = Array.from({ length: total }, (_, index) => on(index)).filter(Boolean).length
  if (filled === 0) return []
  if (filled === total) return [{ start: { day: 0, hour: 0 }, end: { day: 0, hour: 0 }, hours: total, overnight: true }]
  const runs: Run[] = []
  for (let index = 0; index < total; index++) {
    if (!on(index) || on(index - 1)) continue
    let length = 0
    while (on(index + length)) length++
    const startDay = Math.floor(index / HOURS), endIndex = index + length
    const endCell = { day: Math.floor(endIndex / HOURS) % DAYS, hour: endIndex % HOURS }
    // Ending exactly at midnight is the end of the same day, not an overnight shift.
    const overnight = Math.floor((endIndex - 1) / HOURS) !== startDay
    runs.push({ start: { day: startDay, hour: index % HOURS }, end: endCell, hours: length, overnight })
  }
  return runs.sort((a, b) => a.start.day * HOURS + a.start.hour - (b.start.day * HOURS + b.start.hour))
}

/**
 * Adds a shift from `from` to `to` (whole hours, 0–24) on each chosen day. An end at or before the start runs past
 * midnight into the next day (Sunday continues into Monday), e.g. 22 to 6.
 */
export function addShift(grid: Grid, days: number[], from: number, to: number): Grid {
  const next = cloneGrid(grid)
  for (const day of days) {
    const hours = to > from ? to - from : HOURS - from + to
    for (let step = 0; step < hours; step++) {
      const index = day * HOURS + from + step
      next[Math.floor(index / HOURS) % DAYS][index % HOURS] = true
    }
  }
  return next
}
