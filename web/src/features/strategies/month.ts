/** Month fields as two selects: a native month input is a plain text box in Firefox and Safari. */
export const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']

export const splitMonth = (value: string) => ({ year: Number(value.slice(0, 4)), month: Number(value.slice(5, 7)) })
export const joinMonth = (year: number, month: number) => `${year}-${String(month).padStart(2, '0')}`

/** Years ahead the horizon form offers: ten, enough for a rotation (e.g. 2026 through 2036). */
export const FORM_YEARS_AHEAD = 10

/** Years back the form offers: the API takes a start up to five years before this month, to evaluate past seasons. */
export const FORM_YEARS_BACK = 5

/** Five years back through ten years ahead. */
export const yearOptions = (thisYear: number) =>
  Array.from({ length: FORM_YEARS_BACK + FORM_YEARS_AHEAD + 1 }, (_, index) => thisYear - FORM_YEARS_BACK + index)
