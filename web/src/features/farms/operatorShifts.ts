import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '@/shared/api/client'

export type OperatorShift = {
  id: number
  farmId: string
  /** Labour type the week belongs to; null is the farm default week, followed by every type without its own. */
  laborCategoryId: number | null
  month: number
  dayOfWeek: number
  startTime: string
  endTime: string
  createdAtUtc: string
  updatedAtUtc: string
}

export type OperatorShiftInput = Pick<OperatorShift, 'month' | 'dayOfWeek' | 'startTime' | 'endTime'> & { laborCategoryId?: number | null }

const key = (farmId: string) => ['farms', farmId, 'operator-shifts'] as const
const path = (farmId: string) => '/farms/' + farmId + '/operator-shifts'

export function useOperatorShifts(farmId: string) {
  return useQuery({ queryKey: key(farmId), queryFn: () => apiFetch<OperatorShift[]>(path(farmId)) })
}

export function useSaveOperatorShift(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: OperatorShiftInput }) =>
      apiFetch<OperatorShift>(path(farmId) + (id ? '/' + id : ''), {
        method: id ? 'PUT' : 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key(farmId) }),
  })
}

/** One shift of a week template; the months it applies to are chosen by the request. Times are HH:mm, 23:59 ends the day. */
export type ShiftTemplate = Pick<OperatorShift, 'dayOfWeek'> & { startTime: string; endTime: string }

/**
 * Replaces the shifts of the given months with one week template in a single atomic request, for one labour type
 * (null: the farm default week). Other labour types are untouched.
 */
export function useSaveOperatorSchedule(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ months, shifts, laborCategoryId }: { months: number[]; shifts: ShiftTemplate[]; laborCategoryId: number | null }) =>
      apiFetch<OperatorShift[]>(path(farmId) + '/schedule', { method: 'PUT', body: JSON.stringify({ months, shifts, laborCategoryId }) }),
    onSuccess: (saved, { months, laborCategoryId }) => {
      // Show the server's answer at once; refetching alone would flash the old week for a moment.
      queryClient.setQueryData<OperatorShift[]>(key(farmId), shifts => [...(shifts ?? [])
        .filter(shift => shift.laborCategoryId !== laborCategoryId || !months.includes(shift.month)), ...saved])
      return queryClient.invalidateQueries({ queryKey: key(farmId) })
    },
  })
}

export function useDeleteOperatorShift(farmId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => apiFetch<void>(path(farmId) + '/' + id, { method: 'DELETE' }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key(farmId) }),
  })
}
