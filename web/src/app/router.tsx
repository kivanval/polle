import { createBrowserRouter } from 'react-router'
import { LoginPage } from '@/pages/auth/LoginPage'
import { RegisterPage } from '@/pages/auth/RegisterPage'
import { ProtectedRoute } from '@/features/auth/ProtectedRoute'
import { RootLayout } from './RootLayout'
import { ProfilePage } from '@/pages/profile/ProfilePage'
import { NotFoundPage } from '@/pages/NotFoundPage'

export const router = createBrowserRouter([
  { element: <RootLayout />, hydrateFallbackElement: <div className="atlas-state" role="status"><p>Opening your workspace…</p></div>, children: [
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  { element: <ProtectedRoute />, children: [
    { path: '/profile', element: <ProfilePage /> },
  ] },
  { path: '*', element: <NotFoundPage /> },
  ] },
])
