import { useContext } from 'react'
import { Context } from './pageContextValue'

export function useAssistantPageSelection() {
  const value = useContext(Context)
  if (!value) throw new Error('Assistant page context is unavailable')
  return value
}
