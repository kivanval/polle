import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '@/shared/api/client'
import { farmKeys, type Field } from './api'

/** A way of growing a crop (public.crop_profiles), e.g. Wheat as "Winter Grain Harvest". Plantings store its id. */
export type CropProfile = { id: number; systemName: string; isPerennial: boolean }

/** Crop species from the solver catalog (public.crops), with its growing profiles ordered by id. */
export type Crop = { id: number; name: string; nameUk: string | null; scientificName: string | null; profiles: CropProfile[] }

/** ISO calendar dates (YYYY-MM-DD), both endpoints inclusive. Null end reserves the field indefinitely. */
export type PlantingInput = {
  cropProfileId: number // CropProfile.id from GET /api/crops; the API rejects unknown ids.
  seasonYear: number // Grouping label, not the chronological order or a uniqueness key.
  plantingDate: string
  harvestingDate?: string | null
  notes?: string | null
  /** Irrigation planned over the whole season, mm; null or 0 is rain-fed. */
  irrigationMm?: number | null
  /** A product from GET /api/fertilizers and kilograms of it per hectare over the season; both null for none. */
  fertilizerMaterialId?: number | null
  fertilizerRateKgHa?: number | null
  /**
   * The solver's target levels a crop copied from a suggested plan keeps (0-100): how far each irrigation refills the
   * root-zone water, and the share of the nutrient shortfall the fertilizer covers. Null leaves the solver's default.
   */
  irrigationTargetPercent?: number | null
  fertilizerTargetPercent?: number | null
}

/**
 * The target levels a saved crop keeps through an edit: the irrigation level while its water stays the same, the
 * fertilizer level while its product and rate stay the same. A changed or removed input drops its level, since the
 * level belonged to the amount the solver chose.
 */
export function keptTargets(planting: Planting | undefined, irrigationMm: number | null, fertilizerMaterialId: number | null,
  fertilizerRateKgHa: number | null): Pick<PlantingInput, 'irrigationTargetPercent' | 'fertilizerTargetPercent'> {
  const sameWater = irrigationMm !== null && irrigationMm === (planting?.irrigationMm ?? null)
  const sameFertilizer = fertilizerMaterialId !== null && fertilizerMaterialId === (planting?.fertilizerMaterialId ?? null)
    && fertilizerRateKgHa === (planting?.fertilizerRateKgHa ?? null)
  return { irrigationTargetPercent: sameWater ? planting?.irrigationTargetPercent ?? null : null,
    fertilizerTargetPercent: sameFertilizer ? planting?.fertilizerTargetPercent ?? null : null }
}

export type Planting = Omit<PlantingInput, 'harvestingDate' | 'notes' | 'irrigationMm' | 'fertilizerMaterialId' | 'fertilizerRateKgHa'
  | 'irrigationTargetPercent' | 'fertilizerTargetPercent'> & {
  /** Absent from an API older than these fields. */
  irrigationMm?: number | null; fertilizerMaterialId?: number | null; fertilizerRateKgHa?: number | null
  irrigationTargetPercent?: number | null; fertilizerTargetPercent?: number | null
  id: string
  cropId: number // The profile's crop.
  fieldId: string
  harvestingDate: string | null
  notes: string | null
  status: 'planned' | 'current' | 'historical' // Evaluated by the API using today's UTC date.
  createdAtUtc: string
  updatedAtUtc: string
}

/** A fertilizer product with its N, P₂O₅ and K₂O content as on the label, percent. */
export type Fertilizer = { id: number; name: string; nitrogenPercent: number; phosphatePercent: number; potashPercent: number }

export const plantingApi = {
  crops: () => apiFetch<Crop[]>('/crops'),
  fertilizers: () => apiFetch<Fertilizer[]>('/fertilizers'),
  listFarm: (farmId: string) => apiFetch<Planting[]>(`/farms/${farmId}/plantings`),
  list: (fieldId: string) => apiFetch<Planting[]>(`/fields/${fieldId}/plantings`),
  get: (id: string) => apiFetch<Planting>(`/plantings/${id}`),
  create: (fieldId: string, input: PlantingInput) => apiFetch<Planting>(`/fields/${fieldId}/plantings`, { method: 'POST', body: JSON.stringify(input) }),
  update: (id: string, input: PlantingInput) => apiFetch<Planting>(`/plantings/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  remove: (id: string) => apiFetch<void>(`/plantings/${id}`, { method: 'DELETE' }),
}

/** The catalog changes only when the data team updates it; load it once per session. */
export function useCrops() {
  return useQuery({ queryKey: farmKeys.crops, queryFn: plantingApi.crops, staleTime: Infinity })
}

/** The fertilizer products a planting can name; they change only with the catalog. */
export function useFertilizers() {
  return useQuery({ queryKey: ['fertilizers'] as const, queryFn: plantingApi.fertilizers, staleTime: Infinity })
}

/** Planting history of every field in the farm, in one request. Statuses are re-derived by the API on each fetch. */
export function useFarmPlantings(farmId: string) {
  return useQuery({ queryKey: farmKeys.plantings(farmId), queryFn: () => plantingApi.listFarm(farmId) })
}

export function useSavePlanting(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ fieldId, plantingId, input }: { fieldId: string; plantingId?: string; input: PlantingInput }) =>
      plantingId ? plantingApi.update(plantingId, input) : plantingApi.create(fieldId, input),
    onSuccess: planting => queryClient.setQueryData<Planting[]>(farmKeys.plantings(farmId), plantings =>
      plantings?.some(existing => existing.id === planting.id)
        ? plantings.map(existing => existing.id === planting.id ? planting : existing)
        : [...(plantings ?? []), planting]),
    // A 409 means the server's history differs from ours; refetch so the timeline shows what actually blocks the dates.
    onError: () => void queryClient.invalidateQueries({ queryKey: farmKeys.plantings(farmId) }),
  })
}

export function useDeletePlanting(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: plantingApi.remove,
    onSuccess: (_, plantingId) => queryClient.setQueryData<Planting[]>(farmKeys.plantings(farmId),
      plantings => plantings?.filter(planting => planting.id !== plantingId)),
  })
}

/** Season years the user added to plan ahead; years with plantings are listed from the plantings themselves. */
export function usePlanningYears() {
  return useQuery({ queryKey: farmKeys.planningYears, queryFn: () => apiFetch<number[]>('/planning-years') })
}

export function useAddPlanningYear() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (year: number) => apiFetch<number[]>('/planning-years', { method: 'POST', body: JSON.stringify({ year }) }),
    onSuccess: years => queryClient.setQueryData(farmKeys.planningYears, years),
    // A 409 means the year is already stored, e.g. added on another device.
    onError: () => void queryClient.invalidateQueries({ queryKey: farmKeys.planningYears }),
  })
}

export function useRemovePlanningYear() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (year: number) => apiFetch<void>(`/planning-years/${year}`, { method: 'DELETE' }),
    onSuccess: (_, year) => queryClient.setQueryData<number[]>(farmKeys.planningYears, years => years?.filter(value => value !== year)),
    onError: () => void queryClient.invalidateQueries({ queryKey: farmKeys.planningYears }),
  })
}

export const cropName = (crops: Crop[], cropId: number) => crops.find(crop => crop.id === cropId)?.name ?? 'Unknown crop'

/** The crop and growing profile behind a profile id, or null when the catalog no longer has it. */
export function profileOf(crops: Crop[], profileId: number): { crop: Crop; profile: CropProfile } | null {
  for (const crop of crops) {
    const profile = crop.profiles.find(candidate => candidate.id === profileId)
    if (profile) return { crop, profile }
  }
  return null
}
export const currentPlanting = (plantings: Planting[]) => plantings.find(planting => planting.status === 'current') ?? null
export const nextPlanned = (plantings: Planting[]) =>
  plantings.filter(planting => planting.status === 'planned').sort((a, b) => a.plantingDate.localeCompare(b.plantingDate))[0] ?? null

/**
 * What a field's row says grows on it. A parent without a crop of its own summarises its subfields' current crops,
 * e.g. "Growing in 2 subfields: Rapeseed, Soybean", so it never reads as empty while its parts are planted.
 */
export function fieldCropLabel(field: Field, fields: Field[], plantings: Planting[], crops: Crop[]): { text: string; growing: boolean } {
  const own = plantings.filter(planting => planting.fieldId === field.id)
  const current = currentPlanting(own)
  if (current) return { text: cropName(crops, current.cropId), growing: true }
  const inSubfields = fields.filter(child => child.parentFieldId === field.id)
    .flatMap(child => currentPlanting(plantings.filter(planting => planting.fieldId === child.id)) ?? [])
  if (inSubfields.length) {
    const names = [...new Set(inSubfields.map(planting => cropName(crops, planting.cropId)))]
    const shown = names.length > 3 ? `${names.slice(0, 3).join(', ')} +${names.length - 3}` : names.join(', ')
    return { text: `Growing in ${inSubfields.length} ${inSubfields.length === 1 ? 'subfield' : 'subfields'}: ${shown}`, growing: true }
  }
  const planned = nextPlanned(own)
  return { text: planned ? `Planned: ${cropName(crops, planned.cropId)}` : 'Nothing growing now', growing: false }
}
