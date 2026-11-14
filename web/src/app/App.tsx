import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { ServerChallengeError } from '@/shared/api/client'
import { ServerChallengeBanner } from '@/shared/ui/ServerChallengeBanner'
import { router } from './router'

// A request the user dismissed behind a server challenge stays failed; a silent retry would bring the banner back.
const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: (failures, error) => !(error instanceof ServerChallengeError) && failures < 3 } },
})

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <ServerChallengeBanner />
    </QueryClientProvider>
  )
}
