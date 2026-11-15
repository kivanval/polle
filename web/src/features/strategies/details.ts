import type { CycleNutrients, StrategyCycle, StrategySettings } from './api'
import { levelWords } from './choices'
import { cycleRisks, cycleWarnings } from './format'

/** A month as a running count, so months subtract across years. */
export const monthNumber = (month: string) => { const [year, value] = month.split('-').map(Number); return year * 12 + value - 1 }
export const monthFromNumber = (count: number) => `${Math.floor(count / 12)}-${String((count % 12) + 1).padStart(2, '0')}`

export type Stage = 'sowing' | 'growth' | 'harvest'
export const STAGE_LABEL: Record<Stage, string> = { sowing: 'Sowing', growth: 'Growth', harvest: 'Harvest' }
/** Inclusive months of one stage of a crop, YYYY-MM. */
export type StageSpan = { stage: Stage; from: string; to: string }

/**
 * A crop's months split into stages: sowing in its planting month, harvest in its harvest months and from the last
 * harvest until the field is cleared, growth in between. A perennial grows again between its yearly harvests.
 */
export function cycleStages(cycle: Pick<StrategyCycle, 'plantingMonth' | 'clearanceMonth' | 'harvestMonths'>): StageSpan[] {
  const start = monthNumber(cycle.plantingMonth), end = Math.max(start, monthNumber(cycle.clearanceMonth))
  const harvests = new Set(cycle.harvestMonths.map(monthNumber))
  const lastHarvest = Math.max(-1, ...harvests)
  const spans: StageSpan[] = []
  for (let month = start; month <= end; month++) {
    const stage: Stage = month === start ? 'sowing' : harvests.has(month) || (lastHarvest >= 0 && month > lastHarvest) ? 'harvest' : 'growth'
    const last = spans.at(-1)
    if (last?.stage === stage) last.to = monthFromNumber(month)
    else spans.push({ stage, from: monthFromNumber(month), to: monthFromNumber(month) })
  }
  return spans
}

/**
 * One Gantt row: a crop's months (YYYY-MM), its sowing and clearance days (YYYY-MM-DD) when known for the label, risks
 * and warnings; `onSelect` makes the row a button (e.g. to edit a planting).
 */
export type GanttRow = {
  key: string; cropId: number; cropName: string; plantingMonth: string; clearanceMonth: string; harvestMonths: string[]
  plantingDate?: string | null; clearanceDate?: string | null
  risks: string[]; warnings: string[]; onSelect?: () => void
}
export type GanttGroup = { key: string; label: string; rows: GanttRow[]; muted?: boolean }

/** A solver crop as a Gantt row. */
export const cycleRow = (cycle: StrategyCycle, index: number): GanttRow => ({
  key: `${cycle.plantingMonth}-${cycle.cropProfileId}-${index}`, cropId: cycle.cropId, cropName: cycle.cropName, plantingMonth: cycle.plantingMonth,
  clearanceMonth: cycle.clearanceMonth, harvestMonths: cycle.harvestMonths, plantingDate: cycle.plantingDate, clearanceDate: cycle.clearanceDate,
  risks: cycleRisks(cycle), warnings: cycleWarnings(cycle),
})

/** One year column of the chart: twelve month columns when expanded, a single narrow column when collapsed. */
export type GanttYear = { year: number; expanded: boolean; x: number; width: number }
export type GanttScale = { years: GanttYear[]; width: number; monthWidth: number
  /** Left edge of a month (YYYY-MM), or of the month after the last one when `end` is set, in px. */
  x: (month: string, end?: boolean) => number }

export function ganttScale(firstYear: number, lastYear: number, expanded: ReadonlySet<number>, monthWidth = 30, collapsedWidth = 60): GanttScale {
  const years: GanttYear[] = []
  let x = 0
  for (let year = firstYear; year <= lastYear; year++) {
    const width = expanded.has(year) ? monthWidth * 12 : collapsedWidth
    years.push({ year, expanded: expanded.has(year), x, width })
    x += width
  }
  const position = (count: number) => {
    const year = years.find(item => item.year === Math.floor(count / 12))
    if (!year) return count < monthNumber(`${firstYear}-01`) ? 0 : x
    return year.x + ((count % 12) / 12) * year.width
  }
  return { years, width: x, monthWidth, x: (month, end = false) => position(monthNumber(month) + (end ? 1 : 0)) }
}

/** Rain the field typically gets from sowing to clearance, in mm, from its twelve monthly normals (January first). */
export function seasonRainMm(cycle: Pick<StrategyCycle, 'plantingMonth' | 'clearanceMonth'>, monthlyRainMm: readonly number[]): number {
  let total = 0
  for (let month = monthNumber(cycle.plantingMonth); month <= monthNumber(cycle.clearanceMonth); month++) total += monthlyRainMm[month % 12] ?? 0
  return total
}

/** One month of the plan's water: what came in (rain, irrigation) against what the crop used, mm. */
export type WaterMonth = { month: string; rainMm: number | null; irrigationMm: number; useMm: number; cropped: boolean }

/** Whether the solver reported month-by-month water for any crop of the plan. */
export const hasMonthlyWater = (cycles: readonly Pick<StrategyCycle, 'monthlyWater'>[]) => cycles.some(cycle => (cycle.monthlyWater?.length ?? 0) > 0)

/** The months a plan covers, YYYY-MM, both included. */
export type WaterHorizon = { startMonth: string; endMonth: string }

/**
 * Every month of the planning horizon, widened to the first sowing and last clearance when a crop runs past it (without a
 * horizon, just those). Rain is the field's normal for the calendar month (null while unknown), irrigation and crop use
 * come from the solver; a month with no crop on the field is bare (`cropped` false).
 */
export function waterMonths(cycles: readonly Pick<StrategyCycle, 'plantingMonth' | 'clearanceMonth' | 'monthlyWater'>[], monthlyRainMm?: readonly number[],
  horizon?: WaterHorizon): WaterMonth[] {
  if (!cycles.length) return []
  const bounds = horizon ? [monthNumber(horizon.startMonth), monthNumber(horizon.endMonth)].filter(Number.isFinite) : []
  const first = Math.min(...bounds, ...cycles.map(cycle => monthNumber(cycle.plantingMonth)))
  const last = Math.max(...bounds, ...cycles.map(cycle => Math.max(monthNumber(cycle.clearanceMonth), ...(cycle.monthlyWater ?? []).map(entry => monthNumber(entry.month)))))
  const solver = new Map<number, { irrigation: number; use: number }>()
  for (const entry of cycles.flatMap(cycle => cycle.monthlyWater ?? [])) {
    const key = monthNumber(entry.month), sum = solver.get(key) ?? { irrigation: 0, use: 0 }
    solver.set(key, { irrigation: sum.irrigation + entry.irrigationMm, use: sum.use + entry.waterUseMm })
  }
  const cropped = (month: number) => cycles.some(cycle => monthNumber(cycle.plantingMonth) <= month && month <= monthNumber(cycle.clearanceMonth))
  return Array.from({ length: last - first + 1 }, (_, index) => {
    const month = first + index, figures = solver.get(month)
    return { month: monthFromNumber(month), rainMm: monthlyRainMm ? monthlyRainMm[month % 12] ?? 0 : null,
      irrigationMm: figures?.irrigation ?? 0, useMm: figures?.use ?? 0, cropped: cropped(month) }
  })
}

export type Nutrient = 'N' | 'P' | 'K'
export const NUTRIENT_LABEL: Record<Nutrient, string> = { N: 'Nitrogen (N)', P: 'Phosphorus (P)', K: 'Potassium (K)' }
/** One nutrient of one crop, kg/ha: `fixed` is nitrogen legumes take from the air and is zero for P and K. */
export type NutrientFlow = { applied: number; fixed: number; removed: number }

/**
 * One crop's fertilizer as the farmer buys it: the product's name ("Fertilizer" when the catalog has none) and the
 * kilograms of the product itself per hectare over the crop; `rateKgHa` is null on results from before the solver sent it.
 * `targetPercent` is the share of the crop's nutrient deficit it covers, null when the solver didn't say.
 */
export type FertilizerPlan = { product: string; rateKgHa: number | null; targetPercent: number | null }

/**
 * One crop's irrigation: millimetres over its season (the solver's allowance, else what it applied) and how far each
 * watering refills the readily available water (null when the solver didn't say, which it reads as a full refill).
 */
export type WaterPlan = { mm: number; targetPercent: number | null }

/** One step of the plan summary: a crop with its harvest months in order, profit, fertilizer and water (null when it gets none). */
export type SummaryStep = {
  key: string; cycle: StrategyCycle; harvestMonths: string[]; netUsd: number; fertilizer: FertilizerPlan | null; water: WaterPlan | null
}

const productName = (name: string | null | undefined) => name?.trim() || 'Fertilizer'
const level = (value: number | null | undefined) => typeof value === 'number' && value >= 0 && value <= 100 ? value : null

function fertilizerPlan(cycle: StrategyCycle): FertilizerPlan | null {
  const fertilizer = cycle.fertilizer
  if (!fertilizer) return null
  return { product: productName(fertilizer.name), rateKgHa: fertilizer.rateKgHa && fertilizer.rateKgHa > 0 ? fertilizer.rateKgHa : null,
    targetPercent: level(fertilizer.targetPercent) }
}

/** The crop's irrigation: what the solver applied, or its allowance when it reports one without monthly figures; null when rain-fed. */
export function waterPlan(cycle: Pick<StrategyCycle, 'irrigationMm' | 'irrigation'>): WaterPlan | null {
  const mm = cycle.irrigationMm > 0 ? cycle.irrigationMm : cycle.irrigation?.amountMm ?? 0
  return mm > 0 ? { mm, targetPercent: level(cycle.irrigation?.targetPercent) } : null
}

/** Every crop of the plan as one step of the farmer's action plan, in plan order. */
export function summarySteps(cycles: readonly StrategyCycle[]): SummaryStep[] {
  return cycles.map((cycle, index) => ({
    key: `${cycle.plantingMonth}-${cycle.cropProfileId}-${index}`, cycle,
    harvestMonths: [...cycle.harvestMonths].sort(), netUsd: cycle.revenueUsd - cycle.costsUsd, fertilizer: fertilizerPlan(cycle), water: waterPlan(cycle),
  }))
}

/**
 * The share of the crop's potential yield the solver expects after water, nutrient and other stresses, as farm words,
 * e.g. "88% of potential"; null when the figure isn't a usable percentage.
 */
export function expectedYield(yieldPercent: number | null | undefined): string | null {
  if (typeof yieldPercent !== 'number' || !Number.isFinite(yieldPercent) || yieldPercent < 0) return null
  return `${Math.round(Math.min(yieldPercent, 100))}% of potential`
}

/**
 * What a strategy may do as short farm words, e.g. ["Water in 3 steps", "Fertilizer in 4 steps", "Accepts risks",
 * "Keeps the soil fed"]; "Estimates missing figures" only when it does. Without levels (an older API), just on or off.
 */
export function settingsText(settings: StrategySettings): string[] {
  const dosed = (on: boolean, level: string | undefined, name: string) => level && level !== 'OFF' ? `${name} ${levelWords(level)}` : on ? name : null
  return [
    dosed(settings.irrigation, settings.irrigationLevel, 'Water') ?? 'Rain only',
    dosed(settings.fertilizer, settings.fertilizerLevel, 'Fertilizer') ?? 'No fertilizer',
    settings.allowAgronomicRisks ? 'Accepts risks' : 'Avoids risks',
    settings.allowSoilDepletion ? 'May draw the soil down' : 'Keeps the soil fed',
    ...settings.allowMissingData ? ['Estimates missing figures'] : [],
  ]
}

/** One crop's agronomic risks and warnings for the Details panel, in plan order; crops without any are left out. */
export type CropRisks = {
  key: string; cropName: string; plantingMonth: string; clearanceMonth: string; plantingDate?: string | null; clearanceDate?: string | null
  risks: string[]; warnings: string[]
}

export function agronomicRisks(cycles: readonly StrategyCycle[]): CropRisks[] {
  return cycles.map((cycle, index) => ({ key: `${cycle.plantingMonth}-${cycle.cropProfileId}-${index}`, cropName: cycle.cropName,
    plantingMonth: cycle.plantingMonth, clearanceMonth: cycle.clearanceMonth, plantingDate: cycle.plantingDate, clearanceDate: cycle.clearanceDate,
    risks: cycleRisks(cycle), warnings: cycleWarnings(cycle) }))
    .filter(crop => crop.risks.length + crop.warnings.length > 0)
}

export function nutrientFlow(nutrients: CycleNutrients | undefined, nutrient: Nutrient): NutrientFlow {
  if (!nutrients) return { applied: 0, fixed: 0, removed: 0 }
  if (nutrient === 'N') return { applied: nutrients.nitrogenAppliedKgHa, fixed: nutrients.nitrogenFixedKgHa, removed: nutrients.nitrogenRemovedKgHa }
  if (nutrient === 'P') return { applied: nutrients.phosphorusAppliedKgHa, fixed: 0, removed: nutrients.phosphorusRemovedKgHa }
  return { applied: nutrients.potassiumAppliedKgHa, fixed: 0, removed: nutrients.potassiumRemovedKgHa }
}
