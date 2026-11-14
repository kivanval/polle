import { useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { ArrowUpRight, CloudSun, LayoutDashboard, Map, Menu, UserRound, X } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useAuth } from '@/features/auth/useAuth'
import { Brand } from '@/shared/ui/Brand'
import { LogoutButton } from '@/shared/ui/LogoutButton'
import { idPlaceholder, usePageName } from './pageTitle'

// The workspace's sections; each one joins the navigation as it lands.
const sections: { label: string; to: string; icon: LucideIcon; end?: boolean }[] = [
  { label: 'Overview', to: '/dashboard', icon: LayoutDashboard, end: true },
  // Farms and their fields: one workspace (see FarmsWorkspace).
  { label: 'Farms & planning', to: '/dashboard/farms', icon: Map },
  { label: 'Climate intelligence', to: '/dashboard/climate', icon: CloudSun },
]
export function AppShell() {
  const { data: user } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const location = useLocation()
  const path = location.pathname.toLowerCase().replace(/\/+$/, '')
  const section = [...sections].reverse().find(s => path === s.to || path.startsWith(`${s.to}/`))
  const title = path === '/profile' ? 'Account' : section?.label ?? 'Overview'
  // A page for one record (a farm) adds its name after the section: the published name, or "Farm" while it loads, never the id.
  const record = idPlaceholder(path)
  const pageName = usePageName()
  return <div className="app-shell">
    <a className="skip-link" href="#workspace">Skip to workspace</a>
    <header className="app-mobile-header"><Brand light /><button className="menu-button" aria-label={menuOpen ? 'Close navigation' : 'Open navigation'} aria-expanded={menuOpen} aria-controls="workspace-sidebar" onClick={() => setMenuOpen(!menuOpen)}>{menuOpen ? <X /> : <Menu />}</button></header>
    {menuOpen && <button className="sidebar-backdrop" aria-label="Close navigation" onClick={() => setMenuOpen(false)} />}
    <aside id="workspace-sidebar" className={`app-sidebar ${menuOpen ? 'is-open' : ''}`}>
      <Brand light /><span className="sidebar-caption mono">YOUR FIELD ATLAS</span>
      <nav aria-label="Workspace navigation">{sections.map(({ label, to, icon: Icon, end }) => <NavLink key={to} to={to} end={end} onClick={() => setMenuOpen(false)}><Icon size={18} strokeWidth={1.5} />{label}</NavLink>)}
        <div className="sidebar-divider" />
        <NavLink to="/profile" onClick={() => setMenuOpen(false)}><UserRound size={18} strokeWidth={1.5} />Account</NavLink>
      </nav>
      <div className="sidebar-bottom"><div className="sidebar-person"><span className="avatar">{user?.email[0]?.toUpperCase()}</span><div><strong>Your workspace</strong><span title={user?.email}>{user?.email}</span></div></div><LogoutButton /></div>
    </aside>
    <div className="app-main"><header className="app-topbar"><nav aria-label="Breadcrumb"><span>Workspace</span><span className="breadcrumb-separator">/</span>
      {record && section ? <><NavLink to={section.to} end className="breadcrumb-link">{title}</NavLink><span className="breadcrumb-separator">/</span>
          {pageName ? <strong className="breadcrumb-current" aria-current="page" title={pageName}>{pageName}</strong>
            : <strong className="breadcrumb-current is-loading" aria-current="page" aria-busy="true">{record}</strong>}</>
        : <strong>{title}</strong>}</nav><NavLink to="/profile" aria-label="View your profile" className="avatar">{user?.email[0]?.toUpperCase()}</NavLink></header>
      <main id="workspace" className={`workspace-content ${/^\/dashboard\/(farms\/(?!fields$|planning$)[^/]+|climate)$/.test(path) ? 'is-atlas' : ''}`}><Outlet /></main>
      <footer className="workspace-footer mono"><span>Polle / A foundation for what’s next</span><a href="https://www.earthdata.nasa.gov/" target="_blank" rel="noreferrer">Explore Earthdata <ArrowUpRight size={13} /></a></footer>
    </div>
  </div>
}
