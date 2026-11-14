/** Inclusive ISO date span drawn by a timeline. */
export type TimelineRange = { start: string; end: string }

const DAY = 86_400_000
const time = (iso: string) => Date.parse(`${iso}T00:00:00Z`)

/**
 * Whole calendar years from the first planting (or this year) through the last planting or five years ahead,
 * whichever is later, so the coming seasons always have room.
 */
export function timelineRange(plantings: { plantingDate: string; harvestingDate: string | null }[], today: string): TimelineRange {
  const thisYear = Number(today.slice(0, 4))
  const years = plantings.flatMap(planting => [planting.plantingDate, planting.harvestingDate ?? planting.plantingDate]).map(iso => Number(iso.slice(0, 4)))
  const first = Math.min(thisYear, ...years)
  const last = Math.max(thisYear + 5, ...years)
  return { start: `${first}-01-01`, end: `${last}-12-31` }
}

/** Left offset and width of a bar in percent of the range; both ends inclusive, an open end runs to the range end. */
export function barLayout(range: TimelineRange, from: string, to: string | null): { left: number; width: number } {
  const start = time(range.start)
  const total = time(range.end) + DAY - start
  const clamp = (value: number) => Math.min(Math.max(value, 0), total)
  const left = clamp(time(from) - start)
  const right = clamp((to ? time(to) + DAY : total + start) - start)
  return { left: (left / total) * 100, width: (Math.max(right - left, 0) / total) * 100 }
}

/** First day of `fromMonth` through the last day of `toMonth` (both YYYY-MM). */
export function monthRange(fromMonth: string, toMonth: string): { from: string; to: string } {
  const [year, month] = toMonth.split('-').map(Number)
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate()
  return { from: `${fromMonth}-01`, to: `${toMonth}-${String(lastDay).padStart(2, '0')}` }
}

const PALETTE_SIZE = 8
/** A crop's bar colour: one of the eight `--crop-N` tokens, stable per crop id. */
export const cropColour = (cropId: number) => `var(--crop-${((cropId * 2654435761) >>> 0) % PALETTE_SIZE})`

/**
 * Where to draw a crop that has no harvest date yet: the end of its season, or today if it is still growing past
 * that, and never before it is sown. Drawing it to the end of the timeline would read as years on the field.
 */
export function openEnd(planting: { plantingDate: string; seasonYear: number }, today: string): string {
  const sownYear = Number(planting.plantingDate.slice(0, 4))
  const seasonEnd = `${Math.max(planting.seasonYear, sownYear)}-12-31`
  return today > seasonEnd ? today : seasonEnd
}
