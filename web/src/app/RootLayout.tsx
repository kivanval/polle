import { useEffect, useRef, useState } from 'react'
import { Outlet, useLocation } from 'react-router'
import { RefreshCw } from 'lucide-react'
import { reloadOnMissingChunk, watchForNewBuild } from '@/shared/version/appVersion'
import { pageTitle, usePageName } from './pageTitle'

export function RootLayout() {
  const { pathname } = useLocation()
  const name = usePageName()
  // A newer build is deployed: the next page change reloads quietly, and a bar offers to reload now.
  const [stale, setStale] = useState(false)
  const firstPath = useRef(pathname)
  useEffect(() => { window.scrollTo({ top: 0, behavior: 'instant' }) }, [pathname])
  useEffect(() => { document.title = pageTitle(pathname, name) }, [pathname, name])
  useEffect(() => reloadOnMissingChunk(), [])
  useEffect(() => watchForNewBuild(() => setStale(true)), [])
  useEffect(() => {
    if (stale && pathname !== firstPath.current) window.location.reload()
    firstPath.current = pathname
  }, [pathname, stale])
  return <>
    {stale && <div className="update-bar" role="status">
      <span>A new version of Polle is out.</span>
      <button type="button" className="button button-dark" onClick={() => window.location.reload()}><RefreshCw size={14} /><span>Reload</span></button>
    </div>}
    <Outlet />
  </>
}
