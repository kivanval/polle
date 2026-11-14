/** The run of months a vertical calendar shows: from `start` (the 1st of a month), `count` months one under another. */
export type MonthWindow = { start: Date; count: number }

/** Months shown before the one the calendar opens on, so it can scroll down to it and earlier days stay one flick away. */
export const MONTHS_BEFORE = 3
/** Months shown when the calendar opens, and added each time it is extended. */
export const WINDOW_MONTHS = 15, EXTEND_BY = 12

export const firstOfMonth = (date: Date) => new Date(date.getFullYear(), date.getMonth(), 1)
export const addMonths = (date: Date, months: number) => new Date(date.getFullYear(), date.getMonth() + months, 1)
/** Whole months from a's month to b's month: 0 for the same month, negative when b is earlier. */
export const monthsBetween = (a: Date, b: Date) => (b.getFullYear() - a.getFullYear()) * 12 + b.getMonth() - a.getMonth()

/** Keeps a window inside [first, last] month, inclusive, trimming whatever falls outside. */
function clamp(start: Date, count: number, first: Date, last: Date): MonthWindow {
  const from = monthsBetween(first, start) < 0 ? firstOfMonth(first) : firstOfMonth(start)
  const end = addMonths(start, count - 1)
  const to = monthsBetween(end, last) < 0 ? firstOfMonth(last) : end
  return { start: from, count: Math.max(1, monthsBetween(from, to) + 1) }
}

/** The months shown on opening: a few before the anchor (the chosen date's month, or this month) and a year or so after. */
export function openingWindow(anchor: Date, first: Date, last: Date): MonthWindow {
  return clamp(addMonths(anchor, -MONTHS_BEFORE), WINDOW_MONTHS, first, last)
}

/** Adds months above the window, as far back as the first month allowed. */
export const extendEarlier = (window: MonthWindow, first: Date, by = EXTEND_BY) =>
  clamp(addMonths(window.start, -by), window.count + by, first, addMonths(window.start, window.count - 1))

/** Adds months below the window, as far ahead as the last month allowed. */
export const extendLater = (window: MonthWindow, last: Date, by = EXTEND_BY) =>
  clamp(window.start, window.count + by, window.start, last)

/** The window opened on January of a year, for the year menu. */
export const yearWindow = (year: number, first: Date, last: Date) => clamp(new Date(year, 0, 1), 12 + MONTHS_BEFORE, first, last)

export const canExtendEarlier = (window: MonthWindow, first: Date) => monthsBetween(first, window.start) > 0
export const canExtendLater = (window: MonthWindow, last: Date) => monthsBetween(addMonths(window.start, window.count - 1), last) > 0
