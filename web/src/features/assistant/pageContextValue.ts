import { createContext } from 'react'

export type AssistantSelection = { farmId: string; fieldId: string | null } | null
type SelectionContext = {
  selection: AssistantSelection; setSelection: (value: AssistantSelection) => void
  /** Number of open panels (such as map layers) the floating launcher must not cover; it hides while above zero. */
  blockers: number; block: (on: boolean) => void
}
export const Context = createContext<SelectionContext | null>(null)
