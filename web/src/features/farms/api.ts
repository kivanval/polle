import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, apiFetch, isWakingUp } from '@/shared/api/client'
import type { FieldConditions } from './conditions'

export type PointGeometry = { type: 'Point'; coordinates: [longitude: number, latitude: number] }
export type PolygonGeometry = { type: 'Polygon'; coordinates: [longitude: number, latitude: number][][] }

export type Farm = {
  id: string; name: string; description: string | null; location: string | null
  homePoint: PointGeometry | null; budgetUsd: number | null; createdAtUtc: string; updatedAtUtc: string
}
export type FarmInput = { name: string; description: string | null; location: string | null; homePoint: PointGeometry }

export type Field = {
  id: string; farmId: string; name: string; description: string | null
  /** Set for a subfield: a part of a top-level field, drawn inside it. Its area is already in the parent's. */
  parentFieldId: string | null
  areaHectares: number; boundary: PolygonGeometry; createdAtUtc: string; updatedAtUtc: string
  /** False when the field has no soil data (it lies in the sea): the solver cannot model it, so strategies are refused. */
  soilAvailable: boolean
} & Partial<FieldConditions>
/** parentFieldId is read on create only; a field's parent never changes. */
/** The conditions are replaced on every save, like the name: send the field's current ones when only another detail changes. */
export type FieldInput = { name: string; description: string | null; boundary: PolygonGeometry; parentFieldId?: string | null } & FieldConditions

/** Top-level fields only: a subfield's land is part of its parent, so adding both would count it twice. */
export const topLevelFields = (fields: Field[]) => fields.filter(field => !field.parentFieldId)
/** Top-level fields in their order, each followed by its subfields. */
export const nestFields = (fields: Field[]) =>
  topLevelFields(fields).flatMap(parent => [parent, ...fields.filter(field => field.parentFieldId === parent.id)])
export const mappedHectares = (fields: Field[]) => topLevelFields(fields).reduce((sum, field) => sum + field.areaHectares, 0)

export const farmKeys = {
  all: ['farms'] as const,
  farm: (farmId: string) => ['farms', farmId] as const,
  fields: (farmId: string) => ['farms', farmId, 'fields'] as const,
  plantings: (farmId: string) => ['farms', farmId, 'plantings'] as const,
  crops: ['crops'] as const,
  fleet: (farmId: string) => ['farms', farmId, 'fleet'] as const,
  equipmentCategories: ['equipment-categories'] as const,
  laborCategories: ['labor-categories'] as const,
  allocations: (farmId: string) => ['farms', farmId, 'budget-allocations'] as const,
  overview: ['resources-overview'] as const,
  planningYears: ['planning-years'] as const,
}

export function useFarms() {
  return useQuery({ queryKey: farmKeys.all, queryFn: () => apiFetch<Farm[]>('/farms') })
}

export function useFarm(farmId: string) {
  return useQuery({ queryKey: farmKeys.farm(farmId), queryFn: () => apiFetch<Farm>(`/farms/${farmId}`) })
}

export function useFields(farmId: string) {
  return useQuery({ queryKey: farmKeys.fields(farmId), queryFn: () => apiFetch<Field[]>(`/farms/${farmId}/fields`) })
}

export function useCreateFarm() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: FarmInput) => apiFetch<Farm>('/farms', { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: farm => {
      queryClient.setQueryData(farmKeys.farm(farm.id), farm)
      void queryClient.invalidateQueries({ queryKey: farmKeys.all, exact: true })
    },
  })
}

export function useUpdateFarm(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: FarmInput) => apiFetch<Farm>(`/farms/${farmId}`, { method: 'PUT', body: JSON.stringify(input) }),
    onSuccess: farm => {
      queryClient.setQueryData(farmKeys.farm(farm.id), farm)
      void queryClient.invalidateQueries({ queryKey: farmKeys.all, exact: true })
    },
  })
}

export function useRenameFarm(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => apiFetch<Farm>(`/farms/${farmId}/name`, { method: 'PUT', body: JSON.stringify({ name }) }),
    onSuccess: farm => {
      queryClient.setQueryData(farmKeys.farm(farm.id), farm)
      queryClient.setQueryData<Farm[]>(farmKeys.all, farms => farms?.map(existing => existing.id === farm.id ? farm : existing))
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

/** Deletes a farm with its fields, subfields, crop history, budget, machinery, workers and expenses. */
export function useDeleteFarm() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (farmId: string) => apiFetch<void>(`/farms/${farmId}`, { method: 'DELETE' }),
    onSuccess: (_, farmId) => {
      queryClient.setQueryData<Farm[]>(farmKeys.all, farms => farms?.filter(farm => farm.id !== farmId))
      // The farm, its fields and its plantings share the ['farms', farmId] prefix.
      queryClient.removeQueries({ queryKey: farmKeys.farm(farmId) })
      for (const key of [farmKeys.all, farmKeys.overview, ['expenses'], ['workers']]) void queryClient.invalidateQueries({ queryKey: key, exact: key === farmKeys.all })
    },
  })
}

export function useSaveField(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ fieldId, input }: { fieldId?: string; input: FieldInput }) => fieldId
      ? apiFetch<Field>(`/fields/${fieldId}`, { method: 'PUT', body: JSON.stringify(input) })
      : apiFetch<Field>(`/farms/${farmId}/fields`, { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: field => queryClient.setQueryData<Field[]>(farmKeys.fields(farmId), fields =>
      fields?.some(existing => existing.id === field.id)
        ? fields.map(existing => existing.id === field.id ? field : existing)
        : [...(fields ?? []), field]),
  })
}

export function useDeleteField(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (fieldId: string) => apiFetch<void>(`/fields/${fieldId}`, { method: 'DELETE' }),
    // The API deletes the field's subfields, crop history and saved strategies with it; expenses lose their field.
    onSuccess: (_, fieldId) => {
      const gone = new Set([fieldId, ...(queryClient.getQueryData<Field[]>(farmKeys.fields(farmId)) ?? [])
        .filter(field => field.parentFieldId === fieldId).map(field => field.id)])
      queryClient.setQueryData<Field[]>(farmKeys.fields(farmId), fields => fields?.filter(field => !gone.has(field.id)))
      gone.forEach(id => queryClient.removeQueries({ queryKey: ['fields', id] }))
      void queryClient.invalidateQueries({ queryKey: farmKeys.farm(farmId) })
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

/** First server validation message for a key (ProblemDetails `errors`), else the general message. */
export function problemMessage(error: unknown, key?: string) {
  if (!(error instanceof ApiError)) return 'We couldn’t reach Polle. Check your connection and try again.'
  const errors = (error.body as { errors?: Record<string, string[]> } | null)?.errors
  if (key && errors?.[key]?.[0]) return errors[key][0]
  const first = errors && Object.values(errors)[0]?.[0]
  if (first) return first
  // 409 can describe a growing-period conflict, parent deletion or a concurrent change.
  // Preserve the specific API detail instead of assuming that every conflict is a field deletion.
  // A 503 can come from the strategy solver, the host waking up, or our server's database; each says what is down.
  if (problemCode(error) === 'solver_unavailable') return SOLVER_UNAVAILABLE
  if (isWakingUp(error)) return error.message
  if (error.status === 503) return SERVER_UNAVAILABLE
  return error.message
}

export const SOLVER_UNAVAILABLE = 'The planning solver is temporarily unavailable, so plans can’t be suggested right now. Your fields and saved plans are fine. Please try again shortly.'
export const SERVER_UNAVAILABLE = 'Our server is temporarily unavailable. Please try again shortly.'

/** The API's `code` in a ProblemDetails body (solver_unavailable, database_unavailable, server_error…). */
function problemCode(error: ApiError) {
  const body = error.body
  return typeof body === 'object' && body !== null && 'code' in body ? body.code : undefined
}
