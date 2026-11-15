import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, ServerChallengeError, apiFetch } from '@/shared/api/client'
import { farmKeys } from '@/features/farms/api'
import type { Planting } from '@/features/farms/plantings'

/**
 * Options and flags the API may add before this app knows them (e.g. Ivan's planned soil_health / soil_conservation):
 * typed as optional and unknown, so a new one never breaks parsing or rendering.
 */
export type FutureFlags = { soilHealth?: boolean; soilConservation?: boolean; flags?: Record<string, unknown> } & { [extra: string]: unknown }

/** One crop of a strategy. Money in US dollars, months as YYYY-MM, days as YYYY-MM-DD. */
export type StrategyCycle = {
  cropProfileId: number; cropId: number; cropName: string; systemName: string; plantingMonth: string; clearanceMonth: string
  harvestMonths: string[]; revenueUsd: number; costsUsd: number; yieldPercent: number; irrigationMm: number
  /**
   * The sowing and clearance days and the harvest days, now that the solver plans to the day (sowing usually on the
   * 1st, 11th or 21st). Null, absent or empty on results saved when it planned by month: show the months then.
   */
  plantingDate?: string | null; clearanceDate?: string | null; harvestDates?: string[] | null
  /** The solver's plain notes: neither risks nor warnings. */
  notes: string[]
  /** Added and taken off over the cycle, kg/ha; absent on results saved before the API sent it. */
  nutrients?: CycleNutrients
  /** Notes the solver marked as risks and as warnings; absent from an API older than the split. */
  risks?: string[]; warnings?: string[]
  /** Irrigation and crop water use per month; empty for results prepared before the solver reported them. */
  monthlyWater?: MonthlyWater[]
  /** Every note with its importance and category, including categories this app has no wording for yet. */
  noteDetails?: CycleNote[]
  /** The fertilizer the solver chose and the months it goes on; null or absent when the crop gets none (or an older result). */
  fertilizer?: CycleFertilizer | null
  /** Machinery and labour for this crop over the field, already in costsUsd; null or absent when unknown. */
  operatingCostsUsd?: number | null
  /** What costsUsd is made of; null or absent on results from before the solver itemised its costs. */
  costBreakdown?: CostBreakdown | null
  /** The current plan only: the solver left this crop out, so its figures are the API's audit from the crop catalog. */
  estimated?: boolean
  /** The water allowance the solver planned the crop with and its refill level; null or absent when rain-fed or on older results. */
  irrigation?: CycleIrrigation | null
} & FutureFlags

/**
 * `amountMm`: water over the whole cycle, mm. `targetPercent`: how far each irrigation refills the readily available
 * water (0-100); null when the solver didn't say, which it reads as a full refill.
 */
export type CycleIrrigation = { materialId: string | null; amountMm: number; targetPercent: number | null }

/** Costs over the whole field, US dollars: machinery and labour, seed, irrigation water and fertilizer. */
export type CostBreakdown = { operatingUsd: number; plantingUsd: number; waterUsd: number; fertilizerUsd: number }

/** materialId and name are null when only the amounts are known; a name is missing when the product isn't in the materials list. */
export type CycleFertilizer = { materialId: string | null; name: string | null; applications: FertilizerApplication[]
  /** Kilograms of the product itself per hectare over the cycle; null or absent on results from before the solver reported it. */
  rateKgHa?: number | null
  /** The share of the crop's nutrient deficit the fertilizer covers, 0-100; null or absent when the solver didn't say. */
  targetPercent?: number | null }
/** N, P and K one month's fertilizer adds, kg/ha. */
export type FertilizerApplication = { month: string; nitrogenKgHa: number; phosphorusKgHa: number; potassiumKgHa: number }

/** How finely the solver may dose water or fertilizer; OFF gives none. */
export type ReplenishmentLevel = 'OFF' | 'BINARY' | 'TERNARY' | 'QUATERNARY' | 'CONTINUOUS'
export const REPLENISHMENT_LEVELS: readonly ReplenishmentLevel[] = ['OFF', 'BINARY', 'TERNARY', 'QUATERNARY', 'CONTINUOUS']

/** What the solver may do for a strategy. The levels are absent from an API older than presets. */
export type StrategySettings = {
  irrigation: boolean; fertilizer: boolean; allowAgronomicRisks: boolean; allowSoilDepletion: boolean; allowMissingData: boolean
  irrigationLevel?: ReplenishmentLevel | (string & {}); fertilizerLevel?: ReplenishmentLevel | (string & {})
}

export type StrategyPreset = { key: string; name: string; summary: string; settings: StrategySettings }

/** Solver settings the farmer chose for a custom strategy, with an optional name and description (blank: made from the settings). */
export type CustomStrategy = {
  allowMissingData: boolean; allowAgronomicRisks: boolean; allowSoilDepletion: boolean; irrigation: ReplenishmentLevel; fertilizer: ReplenishmentLevel
  name?: string; description?: string
}
/** Job keys of the farmer's own strategies start with this; the API never folds one into a preset. */
export const CUSTOM_KEY_PREFIX = 'custom-'
/** One strategy to run: a preset by key, or custom settings. */
export type StrategyChoice = { preset: string } | CustomStrategy

export type MonthlyWater = { month: string; irrigationMm: number; waterUseMm: number }
export type CycleNote = { message: string; importance: 'risk' | 'warn' | 'info' | string; category: string }

export type CycleNutrients = {
  nitrogenAppliedKgHa: number; nitrogenFixedKgHa: number; nitrogenRemovedKgHa: number
  phosphorusAppliedKgHa: number; phosphorusRemovedKgHa: number; potassiumAppliedKgHa: number; potassiumRemovedKgHa: number
}

/** Typical rain per calendar month at the field (January first, mm): the ten-year baseline the solver plans with. */
export type FieldRainfall = { referenceStartYear: number; referenceEndYear: number; monthlyRainMm: number[] }

export function useFieldRainfall(fieldId: string) {
  return useQuery({
    queryKey: ['fields', fieldId, 'monthly-rain'] as const,
    queryFn: () => apiFetch<FieldRainfall>(`/fields/${fieldId}/climate/monthly-rain`),
    staleTime: Infinity, retry: (failures, error) => !(error instanceof ServerChallengeError) && failures < 1,
  })
}

export type StrategyResult = {
  status: 'OPTIMAL' | 'FEASIBLE' | 'INFEASIBLE' | 'UNKNOWN' | string
  profitUsd: number | null; revenueUsd: number; costsUsd: number; irrigationMm: number; nitrogenBalanceKgHa: number
  cycles: StrategyCycle[]; risks: string[]; dataGaps: string[]
  /** Notes marked as warnings, apart from the risks; absent from an older API. */
  warnings?: string[]
  /** Crop water use over all crops, mm; null or absent when the solver did not report it. */
  waterUseMm?: number | null
  /** Machinery and labour over all crops, already in costsUsd and taken off profitUsd; null or absent when unknown. */
  operatingCostsUsd?: number | null
  /** The crops' itemised costs added up; null or absent on older results. */
  costBreakdown?: CostBreakdown | null
} & FutureFlags

export type StrategyJob = {
  key: string; name: string; summary: string
  /** A state this app does not know yet is shown like a failed one, never read as ready. */
  state: 'processing' | 'ready' | 'failed' | (string & {})
  result: StrategyResult | null; error: string | null
  /** Absent from an API older than the matrix. */
  settings?: StrategySettings | null
  /**
   * Still processing, but the planner hasn't taken the task yet (blocked, throttled or unreachable); the API keeps
   * sending it until it gives up. Absent from an older API.
   */
  waiting?: boolean
  /**
   * The current plan only: the solver left it unscored, so the result is the API's own audit of the plan from the crop
   * catalog, the field's climate and its soil settings (status ESTIMATED). Absent from an older API.
   */
  estimated?: boolean
} & FutureFlags
export type StrategyRun = { id: string; fieldId: string; startMonth: string; endMonth: string; createdAtUtc: string; jobs: StrategyJob[] }
export type SavedStrategy = {
  id: string; key: string; name: string; summary: string; startMonth: string; endMonth: string; savedAtUtc: string
  result: StrategyResult | null; baseline: StrategyResult | null
}

export const BASELINE_KEY = 'current-plan'
// Each poll makes the API check every processing job with the solver. The first checks come every second, so a plan the
// solver scores in a moment shows at once; a run still processing after them is checked unhurriedly.
const FAST_POLL_MS = 1000
const FAST_POLLS = 15
const POLL_MS = 5000

/** How long to wait before checking a run again: false once nothing is processing. `updates` counts the answers so far. */
export const pollDelay = (run: StrategyRun | undefined, updates: number): number | false =>
  !run?.jobs.some(job => job.state === 'processing') ? false : updates < FAST_POLLS ? FAST_POLL_MS : POLL_MS

export const strategyKeys = {
  run: (runId: string) => ['strategy-runs', runId] as const,
  saved: (fieldId: string) => ['fields', fieldId, 'strategies'] as const,
}

/** The presets a run uses by default; they change only with a deploy. */
export function useStrategyPresets() {
  return useQuery({ queryKey: ['strategy-presets'] as const, queryFn: () => apiFetch<StrategyPreset[]>('/strategy-presets'), staleTime: Infinity })
}

/**
 * Starts a run: `planOnly` scores just the current plan ("Evaluate my plan"); otherwise only the strategies run, every
 * preset without `strategies` (or with none), and the plan is not scored.
 */
export function useStartStrategyRun(fieldId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: { startMonth: string; endMonth: string; strategies?: StrategyChoice[]; planOnly?: boolean }) =>
      apiFetch<StrategyRun>(`/fields/${fieldId}/strategy-runs`, { method: 'POST', body: JSON.stringify(request) }),
    onSuccess: run => queryClient.setQueryData(strategyKeys.run(run.id), run),
  })
}

/** Polls while any job is still processing; stops once every card is ready or failed. */
export function useStrategyRun(runId: string | null) {
  return useQuery({
    queryKey: strategyKeys.run(runId ?? ''),
    queryFn: () => apiFetch<StrategyRun>(`/strategy-runs/${runId}`),
    enabled: Boolean(runId),
    // A run the server doesn't have (a remembered id after a reload) won't appear later; other failures may pass.
    retry: (failures, error) => !(error instanceof ApiError && error.status === 404) && !(error instanceof ServerChallengeError) && failures < 3,
    refetchInterval: query => pollDelay(query.state.data, query.state.dataUpdateCount),
  })
}

/** Retries a failed card in the run it belongs to: the plan's evaluation and the strategies are separate runs. */
export function useRetryJob() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ runId, key }: { runId: string; key: string }) =>
      apiFetch<StrategyRun>(`/strategy-runs/${runId}/jobs/${encodeURIComponent(key)}/retry`, { method: 'POST' }),
    onSuccess: run => queryClient.setQueryData(strategyKeys.run(run.id), run),
  })
}

export function useSavedStrategies(fieldId: string) {
  return useQuery({ queryKey: strategyKeys.saved(fieldId), queryFn: () => apiFetch<SavedStrategy[]>(`/fields/${fieldId}/strategies`) })
}

export function useSaveStrategy(fieldId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    /** `planRunId`: the "Evaluate my plan" run of the same months, whose score is kept with the strategy to compare later. */
    mutationFn: ({ runId, jobKey, planRunId }: { runId: string; jobKey: string; planRunId?: string | null }) =>
      apiFetch<SavedStrategy>(`/fields/${fieldId}/strategies`, { method: 'POST', body: JSON.stringify({ runId, jobKey, ...(planRunId && { planRunId }) }) }),
    onSuccess: saved => queryClient.setQueryData<SavedStrategy[]>(strategyKeys.saved(fieldId), list => [saved, ...(list ?? [])]),
  })
}

export function useDeleteStrategy(fieldId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (strategyId: string) => apiFetch<void>(`/strategies/${strategyId}`, { method: 'DELETE' }),
    onSuccess: (_, strategyId) => queryClient.setQueryData<SavedStrategy[]>(strategyKeys.saved(fieldId), list => list?.filter(item => item.id !== strategyId)),
  })
}

/** Makes a saved strategy the field's plan; the farm's planting list is replaced for that field with the server's answer. */
export function useApplyStrategy(field: { id: string; farmId: string }) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (strategyId: string) => apiFetch<Planting[]>(`/strategies/${strategyId}/apply`, { method: 'POST' }),
    onSuccess: plantings => queryClient.setQueryData<Planting[]>(farmKeys.plantings(field.farmId),
      list => [...(list ?? []).filter(planting => planting.fieldId !== field.id), ...plantings]),
    onError: () => void queryClient.invalidateQueries({ queryKey: farmKeys.plantings(field.farmId) }),
  })
}
