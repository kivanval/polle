import type { TimelineBar } from '@/features/farms/FieldTimeline'
import type { Crop, Planting } from '@/features/farms/plantings'
import { cropColour, monthRange, openEnd } from '@/features/farms/timeline'
import { BASELINE_KEY, type StrategyJob, type StrategyResult, type StrategyRun, type StrategySettings } from './api'
import { cycleRisks, cycleWarnings, dateText, whenText } from './format'

export type Badge = 'Highest profit' | 'Least water' | 'Lowest cost' | 'Fewest risks'
export type SortKey = 'profit' | 'water' | 'cost' | 'risks'
export type Deltas = { profit: number | null; costs: number; irrigation: number; risks: number; warnings: number }

/** How a strategy differs from the farmer's current plan; null when there is no plan to compare with. */
export function deltas(result: StrategyResult, baseline: StrategyResult | null): Deltas | null {
  if (!baseline) return null
  return {
    profit: result.profitUsd !== null && baseline.profitUsd !== null ? result.profitUsd - baseline.profitUsd : null,
    costs: result.costsUsd - baseline.costsUsd,
    irrigation: result.irrigationMm - baseline.irrigationMm,
    risks: result.risks.length - baseline.risks.length,
    warnings: (result.warnings?.length ?? 0) - (baseline.warnings?.length ?? 0),
  }
}

/** A finished strategy with a usable plan: only these win badges and lead the sort order. */
export const usable = (job: StrategyJob): job is StrategyJob & { result: StrategyResult } =>
  job.state === 'ready' && job.result !== null && ['OPTIMAL', 'FEASIBLE'].includes(job.result.status) && job.result.cycles.length > 0

/** The solver came back with crops and money for this result, whatever status it gave them. */
export const hasFigures = (result: StrategyResult | null | undefined): result is StrategyResult => !!result && result.cycles.length > 0

/**
 * Which of a run's cards are drawn and which are only named in a line. The farmer's current plan is always drawn, even
 * when the solver couldn't fit or score it, so it can be compared with the strategies; strategies are drawn while
 * pending or once they have a usable plan, and named as failed or as finding no plan otherwise.
 */
export function arrangeCards<T extends StrategyJob>(cards: T[]): { shown: T[]; failed: T[]; noPlan: T[] } {
  const own = (job: T) => job.key === BASELINE_KEY
  return {
    shown: cards.filter(job => own(job) || job.state === 'processing' || usable(job)),
    failed: cards.filter(job => !own(job) && job.state === 'failed'),
    noPlan: cards.filter(job => !own(job) && job.state === 'ready' && !usable(job)),
  }
}

/** The current plan's result to compare the strategies with: only one with figures, never an empty or unscored plan. */
export const comparableBaseline = (job: StrategyJob | undefined): StrategyResult | null =>
  job?.state === 'ready' && hasFigures(job.result) ? job.result : null

export type PlanAndStrategies = {
  /** The current plan's card: from "Evaluate my plan", or from a newer run that scored it with the strategies (the assistant's). */
  planJob: StrategyJob | undefined
  /** The run the plan's card came from, for its months, timeline and retries. */
  planRun: StrategyRun | undefined
  /** The strategies of the last "Suggest strategies" run, without any plan card it carried. */
  strategyJobs: StrategyJob[]
  /** The plan's score the strategies are compared with: only a scored plan of the same months as the strategies. */
  baseline: StrategyResult | null
  /** Both are there but for different months, so the strategies show no comparison. */
  otherMonths: boolean
}

/**
 * Puts the field's two runs together. "Evaluate my plan" and "Suggest strategies" each start their own run, in either
 * order, and neither replaces the other; once both are there for the same months, the strategy cards compare with the plan.
 */
export function combineRuns(planRun: StrategyRun | undefined, strategiesRun: StrategyRun | undefined): PlanAndStrategies {
  const evaluated = planRun?.jobs.find(job => job.key === BASELINE_KEY)
  const carried = strategiesRun?.jobs.find(job => job.key === BASELINE_KEY)
  const useCarried = !!carried && (!evaluated || strategiesRun!.createdAtUtc > planRun!.createdAtUtc)
  const source = useCarried ? strategiesRun : evaluated ? planRun : undefined
  const planJob = useCarried ? carried : evaluated
  const strategyJobs = strategiesRun?.jobs.filter(job => job.key !== BASELINE_KEY) ?? []
  const otherMonths = !!source && !!strategiesRun && strategyJobs.length > 0
    && (source.startMonth !== strategiesRun.startMonth || source.endMonth !== strategiesRun.endMonth)
  return { planJob, planRun: source, strategyJobs, baseline: otherMonths ? null : comparableBaseline(planJob), otherMonths }
}

/**
 * The plan's evaluation is older than the field's crops: one was added or changed after the run that scored the plan
 * (e.g. "Use this plan" replaced them), so its score no longer describes the plan.
 */
export function evaluationOutdated(planRun: Pick<StrategyRun, 'createdAtUtc'> | undefined, plantings: readonly Pick<Planting, 'createdAtUtc' | 'updatedAtUtc'>[]): boolean {
  const evaluated = planRun ? utc(planRun.createdAtUtc) : Number.NaN
  if (!Number.isFinite(evaluated)) return false
  return plantings.some(planting => Math.max(utc(planting.createdAtUtc) || 0, utc(planting.updatedAtUtc) || 0) > evaluated)
}

/**
 * The field's crops as one string that changes whenever a crop is added, edited or removed, so the view can tell the
 * plan changed after its evaluation even when the change left no newer timestamp behind (a deleted crop).
 */
export function planFingerprint(plantings: readonly Pick<Planting, 'id' | 'updatedAtUtc'>[]): string {
  return plantings.map(planting => `${planting.id}@${planting.updatedAtUtc}`).sort().join('|')
}

/** Milliseconds of a UTC timestamp from the API; one without a zone is still UTC, never the browser's local time. */
const utc = (value: string | null | undefined) =>
  !value ? Number.NaN : Date.parse(/(Z|[+-]\d\d:?\d\d)$/i.test(value) || !value.includes('T') ? value : `${value}Z`)

/** The solver scored the current plan but left some crops out, which the API filled in from its audit. */
export const partlyEstimated = (job: StrategyJob): boolean =>
  job.state === 'ready' && !job.estimated && hasFigures(job.result) && job.result.cycles.some(cycle => cycle.estimated)

/**
 * Why the solver didn't score the current plan as it is, in farm words; null when it scored every crop. The plan is
 * scored with the water and fertilizer entered for each crop and only with complete crop figures (StrategyRequests.Baseline), so those are what can
 * stop it. The solver scores whatever crops it can as FEASIBLE, so a plan is never refused as a whole.
 */
export function baselineIssue(job: StrategyJob): string | null {
  if (job.state !== 'ready') return null
  if (job.estimated) return ESTIMATED_PLAN_TEXT
  if (!hasFigures(job.result)) return 'This plan breaks one of the planner’s rules (nutrient balance, water, crop stage lengths or rotation), '
    + 'so it couldn’t be scored. Check the crops’ dates, water and fertilizer, then evaluate it again.'
  if (partlyEstimated(job)) return 'Some crops break the planner’s rules (nutrient balance, water, crop stage lengths or rotation), '
    + 'so the ones marked Estimated show our rough estimate instead.'
  return null
}

/** Why the current plan's figures are an estimate, in two short sentences. */
export const ESTIMATED_PLAN_TEXT = 'This plan breaks one of the planner’s rules (nutrient balance, water, crop stage lengths or rotation), '
  + 'so it couldn’t be scored. Below is our rough estimate instead.'

const metrics: { badge: Badge; key: SortKey; value: (result: StrategyResult) => number | null; best: 'max' | 'min' }[] = [
  { badge: 'Highest profit', key: 'profit', value: result => result.profitUsd, best: 'max' },
  { badge: 'Least water', key: 'water', value: result => result.irrigationMm, best: 'min' },
  { badge: 'Lowest cost', key: 'cost', value: result => result.costsUsd, best: 'min' },
  { badge: 'Fewest risks', key: 'risks', value: result => result.risks.length, best: 'min' },
]

/** Which solver strategies win each comparison. The current plan is the reference, never a winner; ties share a badge. */
export function badges(jobs: StrategyJob[]): Record<string, Badge[]> {
  const contenders = jobs.filter(job => job.key !== BASELINE_KEY && usable(job))
  const won: Record<string, Badge[]> = {}
  for (const metric of metrics) {
    const values = contenders.flatMap(job => { const value = metric.value(job.result!); return value === null ? [] : [{ key: job.key, value }] })
    if (values.length < 2) continue
    const best = metric.best === 'max' ? Math.max(...values.map(item => item.value)) : Math.min(...values.map(item => item.value))
    for (const item of values.filter(candidate => candidate.value === best)) (won[item.key] ??= []).push(metric.badge)
  }
  return won
}

const signature = (result: StrategyResult) => JSON.stringify([result.status, result.profitUsd, result.costsUsd, result.irrigationMm, result.risks, result.dataGaps, result.warnings ?? [],
  result.cycles.map(cycle => [cycle.cropProfileId, cycle.systemName, cycle.plantingMonth, cycle.clearanceMonth, cycle.irrigationMm, cycle.revenueUsd, cycle.costsUsd])])

/**
 * Groups strategies whose plan is identical to another strategy's (e.g. Full inputs and Cash grab landing on the same
 * rotation) under one card. Of identical plans the one earliest in the run is the card: the presets go from the most
 * cautious (Control) to the boldest (Cash grab), so that is the same plan under the strictest limits; `variations`
 * lists the others by that card's key. Presets and the farmer's own strategies count alike, so a custom strategy that
 * lands on a preset's plan joins its card. Only usable strategies are compared; the current plan, pending and failed
 * cards always stand alone.
 */
export function groupJobs<T extends StrategyJob>(jobs: T[]): { cards: T[]; variations: Record<string, T[]> } {
  const cardBySignature = new Map<string, T>()
  const variations: Record<string, T[]> = {}
  const cards: T[] = []
  for (const job of jobs) {
    if (job.key !== BASELINE_KEY && usable(job)) {
      const key = signature(job.result)
      const card = cardBySignature.get(key)
      if (card) { (variations[card.key] ??= []).push(job); continue }
      cardBySignature.set(key, job)
    }
    cards.push(job)
  }
  return { cards, variations }
}

export const distinctJobs = <T extends StrategyJob>(jobs: T[]): T[] => groupJobs(jobs).cards

/** How the card's risk limits read to a farmer; nothing when the API didn't say (an older run). */
export const riskStance = (settings: StrategySettings | null | undefined) =>
  !settings ? '' : settings.allowAgronomicRisks ? 'Accepts agronomic risks.' : 'Safe plan.'

/** The current plan first, then usable strategies best-first by the chosen priority, then unusable, pending and failed cards. */
export function sortCards<T extends StrategyJob>(jobs: T[], by: SortKey): T[] {
  const metric = metrics.find(candidate => candidate.key === by)!
  const rank = (job: T) => job.key === BASELINE_KEY ? 0 : usable(job) ? 1 : job.state === 'ready' ? 2 : job.state === 'processing' ? 3 : 4
  const score = (job: T) => {
    const value = job.result ? metric.value(job.result) : null
    if (value === null) return Number.POSITIVE_INFINITY
    return metric.best === 'max' ? -value : value
  }
  return [...jobs].sort((a, b) => rank(a) - rank(b) || score(a) - score(b))
}

const LEGUMES = new Set(['Peas', 'Soybean', 'Lentil', 'Faba bean', 'Dry bean', 'Alfalfa', 'Clover', 'Chickpea', 'Vetch'])

const list = (names: string[]) => names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`
const monthIndex = (month: string) => { const [year, value] = month.split('-').map(Number); return year * 12 + value - 1 }
const coveredMonths = (result: StrategyResult) => result.cycles.reduce((sum, cycle) => sum + monthIndex(cycle.clearanceMonth) - monthIndex(cycle.plantingMonth) + 1, 0)

/** Its risk stance, then a plain-language reason the strategy looks the way it does, built from its crops and settings. */
export const whyText = (result: StrategyResult, baseline: StrategyResult | null, settings: StrategySettings | null | undefined) =>
  [riskStance(settings), reasonText(result, baseline, settings)].filter(Boolean).join(' ')

/** A plain-language reason the strategy looks the way it does, built from its crops and what the solver was allowed to do. */
export function reasonText(result: StrategyResult, baseline: StrategyResult | null, settings: StrategySettings | null | undefined): string {
  const reasons: string[] = []
  const legumes = [...new Set(result.cycles.map(cycle => cycle.cropName).filter(name => LEGUMES.has(name)))]
  if (legumes.length) reasons.push(`${list(legumes)} add${legumes.length === 1 ? 's' : ''} nitrogen to the soil`)
  if (settings?.irrigation && result.irrigationMm > 0) reasons.push('irrigation closes the water gap')
  if (settings && !settings.irrigation && settings.fertilizer) reasons.push('fertilizer lifts yields without paying for water')
  if (settings && !settings.irrigation && !settings.fertilizer) reasons.push('relies on rainfall only, so costs stay low')
  if (settings?.allowMissingData) reasons.push('crops with missing figures are planned from estimates')
  if (baseline && coveredMonths(result) > coveredMonths(baseline)) reasons.push('the field is bare less often')
  const text = reasons.length ? list(reasons) : 'best result the planner found for these limits'
  return `${text[0].toUpperCase()}${text.slice(1)}.`
}

/**
 * Timeline bars for the farmer's own crops sown inside the horizon (YYYY-MM months, both inclusive): the plantings the
 * current plan was scored from, drawn when the solver sent no crops back for it.
 */
export function plannedLane(plantings: Planting[], crops: Crop[], startMonth: string, endMonth: string, today: string): TimelineBar[] {
  return plantings.filter(planting => planting.plantingDate.slice(0, 7) >= startMonth && planting.plantingDate.slice(0, 7) <= endMonth)
    .sort((a, b) => a.plantingDate.localeCompare(b.plantingDate))
    .map(planting => {
      const end = planting.harvestingDate ?? openEnd(planting, today)
      return { key: planting.id, label: crops.find(crop => crop.id === planting.cropId)?.name ?? 'Unknown crop', from: planting.plantingDate, to: end,
        open: !planting.harvestingDate, colour: cropColour(planting.cropId),
        detail: `${dateText(planting.plantingDate)} – ${planting.harvestingDate ? dateText(end) : 'no harvest date'}` }
    })
}

/** Timeline bars for a strategy's crops, each spanning whole months from sowing to clearance. */
export function strategyLane(result: StrategyResult): TimelineBar[] {
  return result.cycles.map((cycle, index) => {
    const { from, to } = monthRange(cycle.plantingMonth, cycle.clearanceMonth)
    return { key: `${index}-${cycle.cropProfileId}`, label: cycle.cropName, from, to, colour: cropColour(cycle.cropId), warnings: cycleRisks(cycle), cautions: cycleWarnings(cycle),
      detail: `${cycle.systemName ? `${cycle.systemName}, ` : ''}${whenText(cycle.plantingDate, cycle.plantingMonth)} – ${whenText(cycle.clearanceDate, cycle.clearanceMonth)}` }
  })
}
