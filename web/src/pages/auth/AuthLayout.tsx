import { Link } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { Brand } from '@/shared/ui/Brand'
import { AuthForm } from '@/features/auth/AuthForm'

export function AuthLayout({ mode }: { mode: 'register' | 'login' }) {
  const registering = mode === 'register'
  return <main className="auth-layout">
    <aside className="auth-landscape"><img src="/images/sunrise.webp" alt="Sunrise over a patchwork of fields and a winding river" /><div className="auth-brand"><Brand light /></div>
      <div className="auth-landscape-copy"><h2>A clearer view.<br />A better beginning.</h2><p>Earth intelligence, rooted in your land.</p></div>
      <span className="auth-image-caption mono">An imagined landscape. A real possibility.</span>
    </aside>
    <section className="auth-content">
      <Link className="back-home" to="/"><ArrowLeft size={17} />Back to home</Link>
      <div className="auth-inner"><span className="mono section-label">YOUR POLLE ACCOUNT</span>
        <h1>{registering ? <>Start growing<br />with perspective.</> : <>Back to your<br />bigger picture.</>}</h1>
        <p className="auth-intro">{registering ? 'Create your account. Your next chapter starts here.' : 'Welcome back. Your land’s next chapter awaits.'}</p>
        <AuthForm mode={mode} key={mode} />
      </div>
      <p className="auth-footnote">Built for NASA Space Apps Challenge 2026.<br />Independent project, not endorsed by NASA.</p>
    </section>
  </main>
}
