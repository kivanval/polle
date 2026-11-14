import { createBrowserRouter } from 'react-router'
import { RootLayout } from './RootLayout'
import { NotFoundPage } from '@/pages/NotFoundPage'

export const router = createBrowserRouter([
  { element: <RootLayout />, hydrateFallbackElement: <div className="atlas-state" role="status"><p>Opening your workspace…</p></div>, children: [
  { path: '*', element: <NotFoundPage /> },
  ] },
])
