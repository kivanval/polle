import { useSyncExternalStore } from 'react'
import { ShieldAlert } from 'lucide-react'
import { challenge } from '@/shared/api/client'

/**
 * Shown while requests wait behind a Render/Cloudflare challenge page. The requests stay open, so "Retry" finishes
 * them where they started (a form submit, a list load) without reloading the page; "Dismiss" lets them fail instead.
 */
export function ServerChallengeBanner() {
  const held = useSyncExternalStore(challenge.subscribe, challenge.count)
  if (held === 0) return null
  return <div className="challenge-banner" role="alert">
    <ShieldAlert size={20} aria-hidden="true" />
    <p>The server’s security check (Render/Cloudflare) is holding back {held === 1 ? 'a request' : `${held} requests`}. Wait a moment, then retry.</p>
    <div className="challenge-banner-actions">
      <button type="button" className="button button-dark" onClick={challenge.retry}>Retry</button>
      <button type="button" className="button button-quiet" onClick={challenge.cancel}>Dismiss</button>
    </div>
  </div>
}
