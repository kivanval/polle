import { useContext, useEffect } from 'react'
import { Context } from './pageContextValue'

/**
 * Hides the floating assistant while `active`, for panels it would otherwise cover. Pages outside the workspace
 * (such as the public climate map) have no assistant, so the hook does nothing there.
 */
export function useHideAssistant(active: boolean) {
  const block = useContext(Context)?.block
  useEffect(() => {
    if (!active || !block) return
    block(true)
    return () => block(false)
  }, [active, block])
}
