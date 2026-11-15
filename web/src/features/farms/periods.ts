import type { Planting } from './plantings'

export type Season = { value: number; min: number; max: number }
/** Where a new crop can go: its season range, a suggested sowing date and the last allowed harvest day. */
export type Slot = { season: Season; plantingDate: string; maxHarvest: string | null }

export const thisYear = new Date().getUTCFullYear()
export const EARLIEST_SEASON = 1950
export const LATEST_SEASON = thisYear + 5

const OPEN_END = '9999-12-31'
export const overlaps = (startA: string, endA: string | null, startB: string, endB: string | null) =>
  startA <= (endB ?? OPEN_END) && startB <= (endA ?? OPEN_END)

const shiftDay = (iso: string, days: number) => {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}
export const dayAfter = (iso: string) => shiftDay(iso, 1)
export const dayBefore = (iso: string) => shiftDay(iso, -1)

/**
 * Where "Add a crop" starts: the day after the last harvest. When the last crop is still growing (no harvest date) it
 * holds the field from its sowing on, so no day is suggested and the farmer picks a free one before it; the form checks
 * the dates against every crop on the field.
 */
export function newCropSlot(periods: Planting[]): Slot {
  const last = periods.reduce<Planting | undefined>((latest, planting) => !latest || planting.plantingDate > latest.plantingDate ? planting : latest, undefined)
  const start = last?.harvestingDate ? dayAfter(last.harvestingDate) : ''
  const season = Math.min(Math.max(start ? Number(start.slice(0, 4)) : thisYear, EARLIEST_SEASON), LATEST_SEASON)
  return { season: { value: season, min: EARLIEST_SEASON, max: Math.max(LATEST_SEASON, season) }, plantingDate: start, maxHarvest: null }
}

const dateFormat = new Intl.DateTimeFormat('en', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })
export const formatDate = (iso: string) => dateFormat.format(new Date(`${iso}T00:00:00Z`))

export function periodText({ plantingDate, harvestingDate, status }: Planting) {
  if (harvestingDate) return `${formatDate(plantingDate)} – ${formatDate(harvestingDate)}`
  return status === 'planned' ? `From ${formatDate(plantingDate)}` : `Sown ${formatDate(plantingDate)}, still growing`
}
