import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '@/shared/api/client'
import { farmKeys, type Farm } from './api'
import { financeKeys } from '@/features/finance/api'

/** Resources with one farm selected: the one place that farm's budget, machinery and operator hours are edited. */
export const farmResourcesPath = (farmId: string, tab?: 'machinery' | 'hours') =>
  `/dashboard/resources?${new URLSearchParams({ farm: farmId, ...(tab ? { tab } : {}) })}`

/** Machinery type from the data team's equipment_categories. */
export type EquipmentCategory = { id: number; slug: string; name: string; description: string | null }

/** Quantity and historical hour limits remain in responses for compatibility; null means unknown. */
export type FleetItemInput = { equipmentCategoryId: number; unitsCount?: number | null; monthlyHoursPerUnit?: number[] | null; customName: string | null }
export type FleetDetailsInput = Pick<FleetItemInput, 'equipmentCategoryId' | 'customName'>
export type FleetItem = FleetItemInput & {
  unitsCount: number | null; monthlyHoursPerUnit: number[] | null
  id: number; farmId: string
  annualHoursPerUnit: number | null // sum of the monthly limits, computed by the API
  annualHoursTotal: number | null // unitsCount × annualHoursPerUnit
  createdAtUtc: string; updatedAtUtc: string
}

export const resourceApi = {
  categories: () => apiFetch<EquipmentCategory[]>('/equipment-categories'),
  fleet: (farmId: string) => apiFetch<FleetItem[]>(`/farms/${farmId}/fleet`),
  createItem: (farmId: string, input: FleetItemInput) => apiFetch<FleetItem>(`/farms/${farmId}/fleet`, { method: 'POST', body: JSON.stringify(input) }),
  updateItem: (itemId: number, input: FleetItemInput) => apiFetch<FleetItem>(`/fleet/${itemId}`, { method: 'PUT', body: JSON.stringify(input) }),
  updateDetails: (itemId: number, input: FleetDetailsInput) => apiFetch<FleetItem>(`/fleet/${itemId}/details`, { method: 'PUT', body: JSON.stringify(input) }),
  removeItem: (itemId: number) => apiFetch<void>(`/fleet/${itemId}`, { method: 'DELETE' }),
  budget: (farmId: string, budgetUsd: number | null) => apiFetch<Farm>(`/farms/${farmId}/budget`, { method: 'PUT', body: JSON.stringify({ budgetUsd }) }),
}

/** Labour type from the data team's labor_categories (Expert data). */
export type LaborCategory = { id: number; name: string }

export type BudgetCategory = 'seeds' | 'fertilizers' | 'crop_protection' | 'fuel' | 'machinery' | 'labor' | 'irrigation' | 'other'
export type BudgetAllocation = { category: BudgetCategory; amountUsd: number; updatedAtUtc: string }
/** budgetUsd stays the solver's total; categories are a planning breakdown and may exceed it. */
export type BudgetAllocations = {
  farmId: string; budgetUsd: number | null; categories: BudgetCategory[]; allocations: BudgetAllocation[]
  allocatedUsd: number; unallocatedUsd: number | null
}
export type FarmResourceSummary = {
  id: string; name: string; location: string | null; budgetUsd: number | null
  allocations: BudgetAllocation[]; allocatedUsd: number; unallocatedUsd: number | null
  fieldCount: number; areaHectares: number; fleet: FleetItem[]
}
export type ResourceOverview = { farms: FarmResourceSummary[]; budgetCategories: BudgetCategory[] }

export const budgetCategoryLabels: Record<BudgetCategory, string> = {
  seeds: 'Seeds', fertilizers: 'Fertilizers', crop_protection: 'Crop protection', fuel: 'Fuel',
  machinery: 'Machinery and repairs', labor: 'Labour', irrigation: 'Irrigation', other: 'Other expenses',
}
/** One hue family per category, ordered so neighbouring segments stay distinguishable. */
export const budgetCategoryColors: Record<BudgetCategory, string> = {
  seeds: '#5f8f3e', fertilizers: '#a7c957', crop_protection: '#386641', fuel: '#d08c38',
  machinery: '#7a6248', labor: '#4f7cac', irrigation: '#6fb3c8', other: '#9aa39b',
}

export function useLaborCategories() {
  return useQuery({ queryKey: farmKeys.laborCategories, queryFn: () => apiFetch<LaborCategory[]>('/labor-categories'), staleTime: 5 * 60_000 })
}

export function useBudgetAllocations(farmId: string) {
  return useQuery({ queryKey: farmKeys.allocations(farmId), queryFn: () => apiFetch<BudgetAllocations>(`/farms/${farmId}/budget-allocations`) })
}

export function useSetAllocation(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ category, amountUsd }: { category: BudgetCategory; amountUsd: number | null }) =>
      apiFetch<BudgetAllocations>(`/farms/${farmId}/budget-allocations/${category}`, { method: 'PUT', body: JSON.stringify({ amountUsd }) }),
    onSuccess: allocations => {
      queryClient.setQueryData(farmKeys.allocations(farmId), allocations)
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

export function useResourceOverview() {
  return useQuery({ queryKey: farmKeys.overview, queryFn: () => apiFetch<ResourceOverview>('/resources/overview') })
}

export function useEquipmentCategories() {
  return useQuery({ queryKey: farmKeys.equipmentCategories, queryFn: resourceApi.categories, staleTime: Infinity })
}

export function useFleet(farmId: string) {
  return useQuery({ queryKey: farmKeys.fleet(farmId), queryFn: () => resourceApi.fleet(farmId) })
}

export function useSaveFleetItem(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ itemId, input }: { itemId?: number; input: FleetDetailsInput }) =>
      itemId ? resourceApi.updateDetails(itemId, input) : resourceApi.createItem(farmId, input),
    onSuccess: item => {
      queryClient.setQueryData<FleetItem[]>(farmKeys.fleet(farmId), items =>
        items?.some(existing => existing.id === item.id)
          ? items.map(existing => existing.id === item.id ? item : existing)
          : [...(items ?? []), item])
      // The all-farms overview carries its own copy of every farm's fleet.
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

export function useDeleteFleetItem(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: resourceApi.removeItem,
    onSuccess: (_, itemId) => {
      queryClient.setQueryData<FleetItem[]>(farmKeys.fleet(farmId), items => items?.filter(item => item.id !== itemId))
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
      // Expenses linked to the machine keep their record but lose the link (ON DELETE SET NULL).
      void queryClient.invalidateQueries({ queryKey: financeKeys.expenses })
    },
  })
}

export function useUpdateBudget(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (budgetUsd: number | null) => resourceApi.budget(farmId, budgetUsd),
    onSuccess: farm => {
      queryClient.setQueryData(farmKeys.farm(farm.id), farm)
      void queryClient.invalidateQueries({ queryKey: farmKeys.all, exact: true })
      // The remainder of the allocation breakdown is measured against this total.
      void queryClient.invalidateQueries({ queryKey: farmKeys.allocations(farm.id) })
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

const usdWhole = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
const usdCents = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
export const formatUsd = (value: number) => (Number.isInteger(value) ? usdWhole : usdCents).format(value)
