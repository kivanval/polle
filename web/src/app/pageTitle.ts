import { useEffect, useSyncExternalStore } from 'react'

/**
 * The name of the record a page shows (a farm's name on /dashboard/farms/:id), for the browser tab and the top bar.
 * A page sets it while it is open; without it, a path segment that is an id is never shown, only a placeholder.
 */
let current: string | null = null
const listeners = new Set<() => void>()
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } }
const read = () => current

function setPageName(name: string | null) {
  if (current === name) return
  current = name
  listeners.forEach(listener => listener())
}

/** The name the open page published, or null. */
export const usePageName = () => useSyncExternalStore(subscribe, read, read)

/** Publishes the name of what this page shows while it is mounted, e.g. the farm's name once it loads. */
export function useNamedPage(name: string | null) {
  useEffect(() => { setPageName(name) }, [name])
  useEffect(() => () => setPageName(null), [])
}

/** A path segment that is a record id (a UUID, or a number), not a word that names a page. */
const ID_SEGMENT = /^(?:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|\d+)$/i
/** What an id under a section is, while its name is still loading. */
const PLACEHOLDERS: Record<string, string> = { farms: 'Farm', fields: 'Field' }

/** The record a path points at by id: the placeholder for it, or null when the path ends in a page name. */
export function idPlaceholder(pathname: string): string | null {
  const segments = pathname.split('/').filter(Boolean)
  const last = segments.at(-1)
  if (!last || !ID_SEGMENT.test(last)) return null
  return PLACEHOLDERS[segments.at(-2) ?? ''] ?? 'Workspace'
}

/** The browser tab's title: the published name when there is one, never a raw id from the path. */
export function pageTitle(pathname: string, name: string | null): string {
  if (pathname === '/') return 'Polle — Earth intelligence, rooted in your land'
  const segment = pathname.split('/').filter(Boolean).at(-1)
  const label = name?.trim() || idPlaceholder(pathname) || (segment ? segment.charAt(0).toUpperCase() + segment.slice(1) : 'Workspace')
  return `Polle — ${label}`
}
