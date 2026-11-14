import type { StrategyCycle } from './api'

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
const number = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })

/** The API's 422 message for a field with no soil data, shown before the farmer even asks for strategies. */
export const NO_SOIL_MESSAGE = 'We have no soil data for this field’s location, so strategies can’t be modelled. You can still record its crops.'

export const money = (value: number | null) => value === null ? '—' : usd.format(value)
export const signedMoney = (value: number) => `${value > 0 ? '+' : value < 0 ? '−' : '±'}${usd.format(Math.abs(value))}`
/** Irrigation the solver reports as a depth in millimetres, as a volume over the whole field: 1 mm on 1 ha is 10 m³. */
export const waterVolume = (millimetres: number, hectares: number) => millimetres * 10 * hectares
export const cubicMetres = (millimetres: number, hectares: number) => `${number.format(waterVolume(millimetres, hectares))} m³`
export const signedCubicMetres = (millimetres: number, hectares: number) => {
  const volume = waterVolume(millimetres, hectares)
  const rounded = Math.round(volume)
  return `${rounded > 0 ? '+' : rounded < 0 ? '−' : '±'}${number.format(Math.abs(volume))} m³`
}
/**
 * A crop's notes as separate lines for a list: a note that itself packs several messages with ";" is split too, blanks
 * are dropped and repeats are shown once.
 */
export const noteLines = (notes: readonly string[]) =>
  [...new Set(notes.flatMap(note => note.split(';')).map(note => note.trim()).filter(Boolean))]
/** A crop's risks. An API older than the risk/warning split sent every note as a note, so those all read as risks. */
export const cycleRisks = (cycle: StrategyCycle) => noteLines(cycle.risks ?? cycle.notes ?? [])
export const cycleWarnings = (cycle: StrategyCycle) => noteLines(cycle.warnings ?? [])
/** Plain notes, apart from risks and warnings (none on the older shape, where every note was counted as a risk). */
export const cycleNotes = (cycle: StrategyCycle) => cycle.risks ? noteLines(cycle.notes ?? []) : []

/** A solver note category as words: "soil_health" reads "Soil health", whatever new category arrives. */
export const categoryText = (category: string) => {
  const words = category.replace(/[_-]+/g, ' ').trim().toLowerCase()
  return words ? `${words[0].toUpperCase()}${words.slice(1)}` : ''
}

export const signedCount = (value: number) => `${value > 0 ? '+' : value < 0 ? '−' : '±'}${Math.abs(value)}`

/** "good" when the change helps the farmer: more profit, or less cost, water and risk. */
export const tone = (value: number | null, higherIsBetter: boolean) =>
  value === null || value === 0 ? '' : (value > 0) === higherIsBetter ? 'is-good' : 'is-bad'

const monthFormat = new Intl.DateTimeFormat('en', { month: 'short', year: 'numeric', timeZone: 'UTC' })
export const monthText = (month: string) => monthFormat.format(new Date(`${month}-01T00:00:00Z`))

const dayFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
const isDay = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value)
/** A YYYY-MM-DD day as "11 May 2027", a YYYY-MM month as "May 2027". */
export const dateText = (value: string) => isDay(value) ? dayFormat.format(new Date(`${value}T00:00:00Z`)) : monthText(value.slice(0, 7))
/** A crop's date to the day when the solver gave one, else its month, as results saved before it planned by day have. */
export const whenText = (day: string | null | undefined, month: string) => dateText(day && isDay(day) ? day : month)
/** A crop's harvests: the days when the solver gave them, else the months, in order. */
export const harvestDays = (cycle: { harvestMonths: string[]; harvestDates?: string[] | null }) =>
  [...(cycle.harvestDates?.length ? cycle.harvestDates : cycle.harvestMonths)].sort()

/** Years after the start year that the default horizon ends in. */
export const DEFAULT_HORIZON_YEARS = 5

/**
 * Default horizon: next month through December five years later, as Zhenya recommends for rotations. Ending in
 * December keeps late-autumn crops and leaves no bare tail on the timeline; at most 71 months, well inside the thirty
 * years the form and the API allow.
 */
export function defaultHorizon(today = new Date()) {
  const next = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth() + 1, 1))
  const start = next.toISOString().slice(0, 7)
  return { startMonth: start, endMonth: `${next.getUTCFullYear() + DEFAULT_HORIZON_YEARS}-12` }
}
