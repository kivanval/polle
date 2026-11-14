import { createBrowserRouter } from 'react-router'
import { LoginPage } from '@/pages/auth/LoginPage'
import { RegisterPage } from '@/pages/auth/RegisterPage'
import { ProtectedRoute } from '@/features/auth/ProtectedRoute'
import { AppShell } from './AppShell'
import { RootLayout } from './RootLayout'
import { DashboardPage } from '@/pages/dashboard/DashboardPage'
import { ProfilePage } from '@/pages/profile/ProfilePage'
import { NotFoundPage } from '@/pages/NotFoundPage'

export const router = createBrowserRouter([
  { element: <RootLayout />, hydrateFallbackElement: <div className="atlas-state" role="status"><p>Opening your workspace…</p></div>, children: [
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  { element: <ProtectedRoute />, children: [{ element: <AppShell />, children: [
    { path: '/dashboard', element: <DashboardPage /> },
    // Farms and their fields are one workspace with a view switch. Map pages pull in Leaflet; load them on demand.
    { path: '/dashboard/farms', lazy: async () => ({ Component: (await import('@/pages/farms/FarmsWorkspace')).FarmsWorkspace }), children: [
      { index: true, lazy: async () => ({ Component: (await import('@/pages/farms/FarmsPage')).FarmsPage }) },
      { path: 'fields', lazy: async () => ({ Component: (await import('@/pages/fields/FieldsPage')).FieldsPage }) },
    ] },
    { path: '/dashboard/farms/new', lazy: async () => ({ Component: (await import('@/pages/farms/NewFarmPage')).NewFarmPage }) },
    { path: '/profile', element: <ProfilePage /> },
  ] }] },
  { path: '*', element: <NotFoundPage /> },
  ] },
])
