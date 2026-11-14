import type { DroughtCell, DroughtClass, DroughtPoint } from './api'

/** Where a value sits on a fixed scale, as a percentage of its width, held inside the scale. */
export function scalePosition(value: number, min: number, max: number): number {
  if (max <= min) return 0
  return Math.min(100, Math.max(0, ((value - min) / (max - min)) * 100))
}

/** The fixed scales the reading's mini-bars are drawn on, so two places or months read the same way. */
export const SCALES = {
  temperature: { min: -30, max: 45 },
  rain: { min: 0, max: 300 },
  evaporation: { min: 0, max: 250 },
  /** Diverging around zero: a deficit fills left of the middle, a surplus right of it. */
  balance: { min: -200, max: 200 },
  /** The drought index only rates the driest half; 50th and wetter is the end of the scale. */
  percentile: { min: 0, max: 50 },
} as const

/** The percentile scale cut into the drought classes, driest first, each as a share of the 0–50 scale. */
export function percentileBands(classes: DroughtClass[], noDroughtColor: string | null): { label: string; color: string; from: number; to: number }[] {
  const ordered = [...classes].sort((a, b) => a.percentile_max - b.percentile_max)
  const bands = ordered.map((item, index) => ({ label: item.label, color: item.color, from: index === 0 ? 0 : ordered[index - 1].percentile_max, to: item.percentile_max }))
  const last = bands.at(-1)?.to ?? 0
  if (last < SCALES.percentile.max) bands.push({ label: 'No drought', color: noDroughtColor ?? '#e7eadf', from: last, to: SCALES.percentile.max })
  return bands.map(band => ({ ...band, from: scalePosition(band.from, 0, SCALES.percentile.max), to: scalePosition(band.to, 0, SCALES.percentile.max) }))
}

/** The twelve months of the chosen month's year that the timeline has, for the reading's month strip. */
export const yearMonths = (point: DroughtPoint, month: string): DroughtCell[] =>
  point.months.filter(item => item.month.slice(0, 4) === month.slice(0, 4))
