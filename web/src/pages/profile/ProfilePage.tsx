import { Check, ShieldCheck } from 'lucide-react'
import { useAuth } from '@/features/auth/useAuth'
import { LogoutButton } from '@/shared/ui/LogoutButton'

export function ProfilePage() {
  const { data: user } = useAuth()
  if (!user) return null
  const created = new Intl.DateTimeFormat('en', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(user.createdAtUtc))
  return <><div className="workspace-heading"><span className="mono section-label">YOUR ACCOUNT</span><h1>A little about you.</h1><p>Your place in a bigger picture.</p></div>
    <section className="profile-panel"><div className="profile-identity"><span className="avatar avatar-large">{user.email[0]?.toUpperCase()}</span><div><h2>{user.displayName || 'Your Polle account'}</h2><p>{user.email}</p><span className="account-active"><Check size={14} />Account active</span></div></div>
      <dl className="profile-details"><div><dt>Email address</dt><dd>{user.email}</dd></div><div><dt>Member since</dt><dd><time dateTime={user.createdAtUtc}>{created}</time></dd></div><div><dt>Workspace</dt><dd>Personal workspace</dd></div></dl>
      <div className="profile-security"><ShieldCheck size={24} strokeWidth={1.5} /><div><h3>A secure place to start.</h3><p>Your session lasts up to seven days. Logging out ends this session immediately.</p></div></div>
      <div className="profile-actions"><LogoutButton className="button button-outline" /><p>Profile editing and password changes are planned for a future release.</p></div>
    </section></>
}
