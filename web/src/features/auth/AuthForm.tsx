import { useEffect, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { ArrowRight, Eye, EyeOff } from 'lucide-react'
import { ApiError, ServerChallengeError, apiFetch, isWakingUp, wakeApiHost } from '@/shared/api/client'
import { userQueryKey, type User } from './useAuth'
import { returnPath } from './returnPath'
import { validateAuth, type AuthErrors, type AuthValues } from './validation'
import { TractorSpinner } from '@/shared/ui/Tractor'

export function AuthForm({ mode }: { mode: 'login' | 'register' }) {
  const registering = mode === 'register'
  const [values, setValues] = useState<AuthValues>({ email: '', password: '', confirmPassword: '' })
  const [errors, setErrors] = useState<AuthErrors>({})
  const [visible, setVisible] = useState<Record<string, boolean>>({})
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const location = useLocation()
  // The host may be asleep; wake it while the user types, so sign-in does not wait out the whole cold start.
  useEffect(() => wakeApiHost(), [])
  const fields: { name: keyof AuthValues; label: string; placeholder: string }[] = [
    { name: 'email', label: 'Email', placeholder: 'you@domain.com' },
    { name: 'password', label: 'Password', placeholder: registering ? 'Create a password' : 'Enter your password' },
    ...(registering ? [{ name: 'confirmPassword' as const, label: 'Confirm password', placeholder: 'Confirm your password' }] : []),
  ]
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const nextErrors = validateAuth(values, registering)
    setErrors(nextErrors); setError('')
    const firstError = Object.keys(nextErrors)[0]
    if (firstError) { document.getElementById(firstError)?.focus(); return }
    setPending(true)
    try {
      const user = await apiFetch<User>(`/auth/${mode}`, { method: 'POST', body: JSON.stringify({ ...values, email: values.email.trim().toLowerCase() }) })
      // A session may expire without an explicit logout. Do not reuse another account's farm cache.
      queryClient.clear()
      queryClient.setQueryData(userQueryKey, user)
      navigate(returnPath(location.state), { replace: true })
    } catch (reason) {
      if (reason instanceof ApiError) {
        const body = reason.body as { errors?: Record<string, string[]> }
        if (body?.errors) setErrors(Object.fromEntries(Object.entries(body.errors).map(([key, messages]) => [key, messages[0]])))
        // A challenge, or the proxy's waking/timeout answer or a platform error page (not the API's JSON): the host is waking.
        const starting = reason instanceof ServerChallengeError || isWakingUp(reason)
        setError(starting
          ? 'The server is still starting up. Please try again in a moment.'
          : reason.status === 503 ? 'Your workspace is temporarily unavailable. Please try again shortly.' : reason.message)
      } else setError('We couldn’t reach Polle. Check your connection and try again.')
    } finally { setPending(false) }
  }
  return <form onSubmit={submit} noValidate className="auth-form" aria-busy={pending}>
    {fields.map(({ name, label, placeholder }) => <div className="form-field" key={name}>
      <label htmlFor={name}>{label}</label>
      <div className="input-wrap"><input id={name} name={name} value={values[name]}
        type={name === 'email' ? 'email' : visible[name] ? 'text' : 'password'} required
        autoComplete={name === 'email' ? 'email' : registering ? 'new-password' : 'current-password'}
        maxLength={name === 'email' ? 254 : 128} placeholder={placeholder} disabled={pending}
        aria-invalid={Boolean(errors[name])} aria-describedby={errors[name] ? `${name}-error` : name === 'password' && registering ? 'password-help' : undefined}
        onChange={(event) => { setValues({ ...values, [name]: event.target.value }); if (errors[name]) setErrors({ ...errors, [name]: undefined }) }} />
        {name !== 'email' && <button className="password-toggle" type="button" onClick={() => setVisible({ ...visible, [name]: !visible[name] })} aria-label={`${visible[name] ? 'Hide' : 'Show'} ${label.toLowerCase()}`} aria-pressed={Boolean(visible[name])}>{visible[name] ? <EyeOff size={19} /> : <Eye size={19} />}</button>}
      </div>
      {name === 'password' && registering && <p id="password-help" className="input-hint">12–128 characters. Use uppercase, lowercase and a number.</p>}
      {errors[name] && <p id={`${name}-error`} className="field-error" role="alert">{errors[name]}</p>}
    </div>)}
    {error && <div className="form-error" role="alert">{error}</div>}
    <button className="button button-dark auth-submit" disabled={pending} type="submit">{pending ? <><TractorSpinner size={18} />Connecting to server…</> : <>{registering ? 'Create account' : 'Log in'}<ArrowRight size={19} /></>}</button>
    <p className="auth-switch">{registering ? 'Already have an account?' : 'New to Polle?'} <Link to={registering ? '/login' : '/register'}>{registering ? 'Log in' : 'Create an account'}</Link></p>
  </form>
}
