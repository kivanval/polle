import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { LogOut } from 'lucide-react'
import { ApiError, apiFetch } from '@/shared/api/client'
import { userQueryKey } from '@/features/auth/useAuth'

export function LogoutButton({ className = '' }: { className?: string }) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  async function logout() {
    setPending(true); setError('')
    try {
      await apiFetch<void>('/auth/logout', { method: 'POST' })
    } catch (reason) {
      // An expired/revoked session has already been terminated on the server.
      if (!(reason instanceof ApiError && reason.status === 401)) {
        setError('Couldn’t log out. Check your connection and try again.')
        setPending(false)
        return
      }
    }
    queryClient.clear()
    queryClient.setQueryData(userQueryKey, null)
    navigate('/login', { replace: true })
  }
  return <div><button className={`logout-button ${className}`} onClick={logout} disabled={pending}><LogOut size={17} />{pending ? 'Logging out…' : 'Log out'}</button>{error && <p role="alert" className="field-error">{error}</p>}</div>
}
