import { useEffect, useEffectEvent, useRef, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'

/** Slide-over panel in the Expert data style: traps Tab, closes on Escape or the backdrop unless busy, restores focus on close. */
export function Drawer({ kicker, title, busy, onClose, children, footer }: {
  kicker: string; title: string; busy: boolean; onClose: () => void; children: ReactNode; footer: ReactNode
}) {
  const panel = useRef<HTMLElement>(null)
  const escape = useEffectEvent((event: globalThis.KeyboardEvent) => { if (event.key === 'Escape' && !busy) onClose() })
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    const listener = (event: globalThis.KeyboardEvent) => escape(event)
    document.addEventListener('keydown', listener)
    return () => { document.removeEventListener('keydown', listener); requestAnimationFrame(() => opener?.isConnected && opener.focus()) }
  }, [])

  function onKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Tab' || !panel.current) return
    const focusable = [...panel.current.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)')]
    const first = focusable[0], last = focusable.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }

  // Rendered at the page root: a drawer opened from a docked panel (the field inspector's crop plan) must not be clipped
  // or stacked under the workspace chrome by that panel's blur. On the server there is no body, so it renders in place.
  const content = <>
    <button type="button" className="catalog-backdrop" aria-label="Close the panel" tabIndex={-1} onClick={() => { if (!busy) onClose() }} />
    <aside ref={panel} className="catalog-drawer finance-drawer" role="dialog" aria-modal="true" aria-labelledby="finance-drawer-title" onKeyDown={onKeyDown}>
      <header className="catalog-drawer-head">
        <div><span className="catalog-drawer-kicker">{kicker}</span><h2 id="finance-drawer-title">{title}</h2></div>
        <button type="button" className="button button-quiet button-icon" aria-label="Close" onClick={onClose} disabled={busy}><X size={16} /></button>
      </header>
      {children}
      {footer}
    </aside>
  </>
  return typeof document === 'undefined' ? content : createPortal(content, document.body)
}
