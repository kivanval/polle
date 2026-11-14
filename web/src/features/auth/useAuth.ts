import { useQuery } from '@tanstack/react-query'
import { ApiError, apiFetch } from '@/shared/api/client'

export type User = { id: string; email: string; displayName: string | null; createdAtUtc: string }
export const userQueryKey = ['current-user'] as const

export function useAuth() {
  return useQuery({
    queryKey: userQueryKey,
    queryFn: async () => {
      try { return await apiFetch<User>('/auth/me') }
      catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    retry: false,
    staleTime: 30_000,
    refetchOnWindowFocus: 'always',
  })
}
