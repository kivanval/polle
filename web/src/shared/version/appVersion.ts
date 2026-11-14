/**
 * Polle is one page: a tab opened before a deploy keeps running the old code, even across page changes, until it is
 * reloaded. Each build writes its id to /version.json and into the code (__BUILD_ID__); the app compares the two when the
 * tab comes back into view and every few minutes, so an old tab picks up the new version instead of showing the old design.
 */

declare const __BUILD_ID__: string

export const BUILD_ID: string = typeof __BUILD_ID__ === 'string' ? __BUILD_ID__ : 'dev'

const CHECK_MS = 5 * 60_000

/** The id of the build the server has now; null when it can't be read (offline, a dev server). */
export async function deployedBuild(): Promise<string | null> {
  try {
    const response = await fetch(`/version.json?t=${Date.now()}`, { cache: 'no-store' })
    if (!response.ok) return null
    const body = await response.json() as { build?: unknown }
    return typeof body.build === 'string' ? body.build : null
  } catch { return null }
}

/** True when the server has a build other than the one running; a dev build or an unreadable answer is never stale. */
export const isStale = (running: string, deployed: string | null) => running !== 'dev' && deployed !== null && deployed !== running

/** Calls `onStale` once a newer build is deployed; returns the cleanup. */
export function watchForNewBuild(onStale: () => void): () => void {
  let done = false
  const check = async () => {
    if (done || document.visibilityState !== 'visible') return
    if (isStale(BUILD_ID, await deployedBuild())) { done = true; onStale() }
  }
  const onVisible = () => void check()
  document.addEventListener('visibilitychange', onVisible)
  window.addEventListener('focus', onVisible)
  const timer = window.setInterval(() => void check(), CHECK_MS)
  void check()
  return () => { done = true; document.removeEventListener('visibilitychange', onVisible); window.removeEventListener('focus', onVisible); window.clearInterval(timer) }
}

const RELOADED_KEY = 'polle-chunk-reload'

/**
 * A part of the old build that the server no longer has (vite:preloadError) means a deploy happened: reload once to load
 * the new build. A second failure within a minute is left to the error screens, so a real outage never loops.
 */
export function reloadOnMissingChunk(): () => void {
  const handler = (event: Event) => {
    let last = 0
    try { last = Number(sessionStorage.getItem(RELOADED_KEY) ?? 0) } catch { /* storage blocked */ }
    if (Date.now() - last < 60_000) return
    try { sessionStorage.setItem(RELOADED_KEY, String(Date.now())) } catch { /* storage blocked */ }
    event.preventDefault()
    window.location.reload()
  }
  window.addEventListener('vite:preloadError', handler)
  return () => window.removeEventListener('vite:preloadError', handler)
}
