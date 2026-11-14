import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '@/shared/api/client'
import { farmKeys } from '@/features/farms/api'
import type { BudgetCategory } from '@/features/farms/resources'
import type { ShiftTemplate } from '@/features/farms/operatorShifts'
import { withSavedExpense } from './cache'

export type PayType = 'hourly' | 'monthly'

export type WorkerShift = { id: number; month: number; dayOfWeek: number; startTime: string; endTime: string }
export type Worker = {
  id: number; farmId: string; fullName: string; position: string | null; laborCategoryId: number | null; phone: string | null
  payType: PayType; payRateUsd: number; hiredOn: string | null; isActive: boolean; notes: string | null
  /** The worker's own recurring week, per month. */
  shifts: WorkerShift[]
  createdAtUtc: string; updatedAtUtc: string
}
export type WorkerInput = Pick<Worker, 'fullName' | 'position' | 'laborCategoryId' | 'phone' | 'payType' | 'payRateUsd' | 'hiredOn' | 'isActive' | 'notes'>

export type Expense = {
  id: number; farmId: string; spentOn: string; category: BudgetCategory; description: string
  quantity: number | null; unit: string | null; unitPriceUsd: number | null; amountUsd: number
  supplier: string | null; documentNumber: string | null; isPaid: boolean
  fieldId: string | null; workerId: number | null; equipmentId: number | null; notes: string | null
  createdAtUtc: string; updatedAtUtc: string
  /** A cost of the whole year (a lease, insurance); spentOn is then January 1. Absent from an older API. */
  allYear?: boolean
}
/** With quantity and unit price both set, the API stores their product rounded to cents and ignores amountUsd. */
export type ExpenseInput = Omit<Expense, 'id' | 'farmId' | 'amountUsd' | 'createdAtUtc' | 'updatedAtUtc'> & { amountUsd: number | null }

export const financeKeys = {
  workers: ['workers'] as const,
  expenses: ['expenses'] as const,
  expensesOfYear: (year: number) => ['expenses', year] as const,
}

export function useWorkers() {
  return useQuery({ queryKey: financeKeys.workers, queryFn: () => apiFetch<Worker[]>('/workers') })
}

export function useSaveWorker() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ workerId, farmId, input }: { workerId?: number; farmId: string; input: WorkerInput }) => workerId
      ? apiFetch<Worker>(`/workers/${workerId}`, { method: 'PUT', body: JSON.stringify(input) })
      : apiFetch<Worker>(`/farms/${farmId}/workers`, { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: worker => queryClient.setQueryData<Worker[]>(financeKeys.workers, workers =>
      workers?.some(existing => existing.id === worker.id)
        ? workers.map(existing => existing.id === worker.id ? worker : existing)
        : [...(workers ?? []), worker].sort((a, b) => a.fullName.localeCompare(b.fullName))),
  })
}

export function useDeleteWorker() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (workerId: number) => apiFetch<void>(`/workers/${workerId}`, { method: 'DELETE' }),
    onSuccess: (_, workerId) => {
      queryClient.setQueryData<Worker[]>(financeKeys.workers, workers => workers?.filter(worker => worker.id !== workerId))
      // Payments to the worker stay on record without the link.
      void queryClient.invalidateQueries({ queryKey: financeKeys.expenses })
    },
  })
}

export function useSaveWorkerSchedule(workerId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ months, shifts }: { months: number[]; shifts: ShiftTemplate[] }) =>
      apiFetch<Worker>(`/workers/${workerId}/schedule`, { method: 'PUT', body: JSON.stringify({ months, shifts }) }),
    onSuccess: worker => queryClient.setQueryData<Worker[]>(financeKeys.workers, workers => workers?.map(existing => existing.id === worker.id ? worker : existing)),
  })
}

export function useExpenses(year: number) {
  return useQuery({
    queryKey: financeKeys.expensesOfYear(year),
    queryFn: () => apiFetch<Expense[]>(`/expenses?from=${year}-01-01&to=${year}-12-31`),
  })
}

export function useSaveExpense() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ expenseId, farmId, input }: { expenseId?: number; farmId: string; input: ExpenseInput }) => expenseId
      ? apiFetch<Expense>(`/expenses/${expenseId}`, { method: 'PUT', body: JSON.stringify(input) })
      : apiFetch<Expense>(`/farms/${farmId}/expenses`, { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: saved => {
      // Put the saved line into every cached year first, so the ledger shows it the moment the drawer closes,
      // then refetch to pick up anything else that changed on the server.
      for (const [queryKey, expenses] of queryClient.getQueriesData<Expense[]>({ queryKey: financeKeys.expenses }))
        if (expenses) queryClient.setQueryData(queryKey, withSavedExpense(expenses, saved, Number(queryKey[1])))
      void queryClient.invalidateQueries({ queryKey: financeKeys.expenses })
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

export function useDeleteExpense() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (expenseId: number) => apiFetch<void>(`/expenses/${expenseId}`, { method: 'DELETE' }),
    onSuccess: (_, expenseId) => {
      queryClient.setQueriesData<Expense[]>({ queryKey: financeKeys.expenses }, expenses => expenses?.filter(expense => expense.id !== expenseId))
      void queryClient.invalidateQueries({ queryKey: financeKeys.expenses })
      void queryClient.invalidateQueries({ queryKey: farmKeys.overview })
    },
  })
}

export const expenseInput = (expense: Expense): ExpenseInput => ({
  spentOn: expense.spentOn, category: expense.category, description: expense.description, quantity: expense.quantity, unit: expense.unit,
  unitPriceUsd: expense.unitPriceUsd, amountUsd: expense.amountUsd, supplier: expense.supplier, documentNumber: expense.documentNumber,
  isPaid: expense.isPaid, fieldId: expense.fieldId, workerId: expense.workerId, equipmentId: expense.equipmentId, notes: expense.notes,
  allYear: expense.allYear ?? false,
})

/** Turns a ProblemDetails `errors` map into one message per field. */
export function fieldErrors(reason: unknown): Record<string, string> | null {
  const errors = (reason as { body?: { errors?: Record<string, string[]> } }).body?.errors
  return errors ? Object.fromEntries(Object.entries(errors).map(([key, messages]) => [key, messages[0]])) : null
}
