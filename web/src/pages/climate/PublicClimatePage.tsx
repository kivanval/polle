import { Link } from 'react-router'
import { ArrowUpRight } from 'lucide-react'
import { useAuth } from '@/features/auth/useAuth'
import { Brand } from '@/shared/ui/Brand'
import { ClimatePage } from './ClimatePage'

/** Climate Intelligence for guests: the same world drought map, without any farm or account data. */
export function PublicClimatePage() {
  const { data: user } = useAuth()
  return <div className="public-climate">
    <header className="public-climate-bar">
      <Brand light />
      <span className="public-climate-title mono">CLIMATE INTELLIGENCE</span>
      <nav aria-label="Account">{user
        ? <Link className="button button-lime" to="/dashboard/climate">Open in your workspace <ArrowUpRight size={15} /></Link>
        : <><Link className="public-climate-login" to="/login">Log in</Link>
          <Link className="button button-lime" to="/register">Read it at your farm <ArrowUpRight size={15} /></Link></>}</nav>
    </header>
    <main className="public-climate-main"><ClimatePage /></main>
  </div>
}
