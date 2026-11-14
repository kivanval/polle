import { createBrowserRouter, redirect } from 'react-router'
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
  // Public: global drought and weather data only. Signed-in users also reach the same page inside the workspace.
  { path: '/climate', lazy: async () => ({ Component: (await import('@/pages/climate/PublicClimatePage')).PublicClimatePage }) },
  { element: <ProtectedRoute />, children: [{ element: <AppShell />, children: [
    { path: '/dashboard', element: <DashboardPage /> },
    // Farms, Fields and Planning are one workspace with a view switch. Map pages pull in Leaflet; load them on demand.
    { path: '/dashboard/farms', lazy: async () => ({ Component: (await import('@/pages/farms/FarmsWorkspace')).FarmsWorkspace }), children: [
      { index: true, lazy: async () => ({ Component: (await import('@/pages/farms/FarmsPage')).FarmsPage }) },
      { path: 'fields', lazy: async () => ({ Component: (await import('@/pages/fields/FieldsPage')).FieldsPage }) },
      { path: 'planning', lazy: async () => ({ Component: (await import('@/pages/rotation/RotationPage')).RotationPage }) },
    ] },
    // The views' old addresses, kept for bookmarks and saved links; the query (?farm=, ?plan=…) comes along.
    { path: '/dashboard/fields', loader: ({ request }) => redirect(`/dashboard/farms/fields${new URL(request.url).search}`) },
    { path: '/dashboard/rotation', loader: ({ request }) => redirect(`/dashboard/farms/planning${new URL(request.url).search}`) },
    { path: '/dashboard/farms/new', lazy: async () => ({ Component: (await import('@/pages/farms/NewFarmPage')).NewFarmPage }) },
    // A farm's resources are edited in Resources with that farm selected; old links land there.
    { path: '/dashboard/farms/:farmId/resources', loader: ({ params }) => redirect(`/dashboard/resources?${new URLSearchParams({ farm: params.farmId! })}`) },
    { path: '/dashboard/farms/:farmId', lazy: async () => ({ Component: (await import('@/pages/farms/FarmAtlasPage')).FarmAtlasPage }) },
    { path: '/dashboard/resources', lazy: async () => ({ Component: (await import('@/pages/resources/ResourcesPage')).ResourcesPage }) },
    { path: '/dashboard/climate', lazy: async () => ({ Component: (await import('@/pages/climate/ClimatePage')).ClimatePage }) },
    { path: '/admin', lazy: async () => ({ Component: (await import('@/pages/admin/CatalogPage')).CatalogPage }) },
    { path: '/profile', element: <ProfilePage /> },
  ] }] },
  { path: '*', element: <NotFoundPage /> },
  ] },
])
