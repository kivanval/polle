import { useState } from 'react'

/** Whether a section of the Details view is unfolded, open at first; `toggle` folds or unfolds it. */
export function useFold(initiallyOpen = true) {
  const [open, setOpen] = useState(initiallyOpen)
  return { open, toggle: () => setOpen(value => !value) }
}
