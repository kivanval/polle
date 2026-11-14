import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { ServerChallengeError, onSessionExpired } from '@/shared/api/client'
import { userQueryKey } from '@/features/auth/useAuth'
import { ServerChallengeBanner } from '@/shared/ui/ServerChallengeBanner'
import { router } from './router'

// A request the user dismissed behind a server challenge stays failed; a silent retry would bring the banner back.
const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: (failures, error) => !(error instanceof ServerChallengeError) && failures < 3 } },
})

// A 401 from any request, including direct apiFetch calls outside React Query, means the session expired or was
// revoked elsewhere. Re-checking /auth/me turns the cached user into null, and the protected routes then send the
// user to sign in (and back afterwards) instead of leaving an error on the page.
onSessionExpired(() => void queryClient.invalidateQueries({ queryKey: userQueryKey }))

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <ServerChallengeBanner />
    </QueryClientProvider>
  )
}
