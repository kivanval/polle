import type { ReactNode } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'

/**
 * A section heading's text as a button that folds the section away and back, with a chevron pointing down while open
 * and right while folded. Goes inside the section's own heading element, so the heading keeps its level and id; the
 * state comes from `useFold`.
 */
export function FoldButton({ open, onToggle, children }: { open: boolean; onToggle: () => void; children: ReactNode }) {
  const Chevron = open ? ChevronDown : ChevronRight
  return <button type="button" className="fold-toggle" aria-expanded={open} onClick={onToggle}>
    <Chevron size={16} aria-hidden="true" className="fold-chevron" /><span>{children}</span>
  </button>
}
