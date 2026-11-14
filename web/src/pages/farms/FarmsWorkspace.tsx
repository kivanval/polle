import { NavLink, Outlet } from 'react-router'
import { CalendarRange, Grid2X2, Map } from 'lucide-react'

/** The three views of one workspace: farms hold fields, and fields hold plans. */
const FARM_VIEWS = [
  { label: 'Farms', to: '/dashboard/farms', icon: Map, end: true },
  { label: 'Fields', to: '/dashboard/farms/fields', icon: Grid2X2, end: false },
  { label: 'Planning', to: '/dashboard/farms/planning', icon: CalendarRange, end: false },
] as const

/**
 * Farms, Fields and Planning as one workspace with a view switch on top. A farm's map, its Plan & strategies and a
 * strategy's Details open full screen from any of the three.
 */
export function FarmsWorkspace() {
  return <div className="farms-workspace">
    <nav className="workspace-tabs" aria-label="Farms, fields and planning">
      {FARM_VIEWS.map(({ label, to, icon: Icon, end }) => <NavLink key={to} to={to} end={end}>
        <Icon size={15} strokeWidth={1.75} aria-hidden="true" />{label}</NavLink>)}
    </nav>
    <Outlet />
  </div>
}
