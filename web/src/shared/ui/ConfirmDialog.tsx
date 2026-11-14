import { useEffect, useId, useRef, type ReactNode } from 'react'
import { TractorSpinner } from './Tractor'

type ConfirmDialogProps = {
  title: string
  children: ReactNode
  confirmLabel: string
  cancelLabel?: string
  icon?: ReactNode
  busy?: boolean
  error?: string
  onConfirm: () => void
  onCancel: () => void
}

/**
 * A modal question with a destructive answer. The native dialog sits in the browser's top layer, above the map and
 * every overlay, keeps focus inside, and closes on Esc; focus starts on the safe answer.
 */
export function ConfirmDialog({ title, children, confirmLabel, cancelLabel = 'Cancel', icon, busy = false, error, onConfirm, onCancel }: ConfirmDialogProps) {
  const ref = useRef<HTMLDialogElement>(null)
  const id = useId()
  useEffect(() => {
    const dialog = ref.current
    if (dialog && !dialog.open) dialog.showModal?.()
    return () => dialog?.close?.()
  }, [])
  return <dialog ref={ref} className="confirm-dialog" role="alertdialog" aria-labelledby={`${id}-title`} aria-describedby={`${id}-body`}
    onCancel={event => { event.preventDefault(); if (!busy) onCancel() }}>
    <h2 id={`${id}-title`}>{title}</h2>
    <div id={`${id}-body`} className="confirm-dialog-body">{children}</div>
    {error && <p className="field-error" role="alert">{error}</p>}
    <div className="field-actions">
      <button type="button" className="button button-danger" disabled={busy} onClick={onConfirm}>{busy ? <TractorSpinner size={15} /> : icon}{confirmLabel}</button>
      <button type="button" className="button button-quiet" autoFocus disabled={busy} onClick={onCancel}>{cancelLabel}</button>
    </div>
  </dialog>
}
