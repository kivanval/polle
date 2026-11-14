import { useCallback, useState, type ReactNode } from 'react'
import { Context, type AssistantSelection } from './pageContextValue'

export function AssistantPageProvider({ children }: { children: ReactNode }) {
  const [selection, setSelection] = useState<AssistantSelection>(null)
  const [blockers, setBlockers] = useState(0)
  const block = useCallback((on: boolean) => setBlockers(count => Math.max(0, count + (on ? 1 : -1))), [])
  return <Context.Provider value={{ selection, setSelection, blockers, block }}>{children}</Context.Provider>
}
