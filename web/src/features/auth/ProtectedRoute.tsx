import { Navigate, Outlet, useLocation } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth, userQueryKey } from './useAuth'
import { Brand } from '@/shared/ui/Brand'
import { TractorLoader } from '@/shared/ui/Tractor'

export function ProtectedRoute() {
  const { data: user, isPending, error, refetch } = useAuth()
  const queryClient = useQueryClient()
  const location = useLocation()
  if (isPending) return <main className="route-state"><Brand /><TractorLoader /><p>Opening your workspace…</p></main>
  // The session check failed for another reason than an expired session (network, server error). Besides retrying,
  // the user can drop the cached session and sign in again; the redirect below then keeps the page asked for.
  if (error) return <main className="route-state"><Brand /><h1>Let’s reconnect.</h1><p>We couldn’t reach your workspace. Check your connection and try again.</p>
    <div className="route-state-actions">
      <button className="button button-dark" onClick={() => void refetch()}>Try again</button>
      <button className="button button-quiet" onClick={() => queryClient.setQueryData(userQueryKey, null)}>Log in again</button>
    </div></main>
  // Only a confirmed missing or expired session leads to sign-in; the page asked for is restored afterwards.
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return <Outlet />
}
