import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useAuth } from './useAuth'
import { returnPath } from './returnPath'

/**
 * Sign-in and registration for guests. A visitor who still has a valid session goes straight back into the workspace
 * instead of being asked to sign in again; while the session check runs the form is shown as usual.
 */
export function GuestRoute({ children }: { children: ReactNode }) {
  const { data: user } = useAuth()
  const location = useLocation()
  if (user) return <Navigate to={returnPath(location.state)} replace />
  return children
}
