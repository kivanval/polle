import { ClipboardCheck, Settings, Sprout } from 'lucide-react'
import { TractorSpinner } from '@/shared/ui/Tractor'

/**
 * The Planning panel's actions, after the From and To months: the two launch actions side by side (Evaluate my plan,
 * then Suggest strategies), and only then the strategy settings, which belong to Suggest strategies and so sit right of
 * it, never between the two actions. Suggest strategies is the form's submit button.
 */
export function PlanningActions({ disabled, describedBy, suggesting, evaluating, customizing, choiceLabel, onEvaluate, onCustomize }: {
  disabled: boolean
  describedBy?: string
  /** A run of strategies is being started. */
  suggesting: boolean
  /** Only the current plan is being evaluated. */
  evaluating: boolean
  customizing: boolean
  /** "6 presets" or "3 chosen strategies"; null until the presets load. */
  choiceLabel: string | null
  onEvaluate: () => void
  onCustomize: () => void
}) {
  return <>
    <button type="button" className="button button-secondary" disabled={disabled} aria-describedby={describedBy} onClick={onEvaluate}>
      {evaluating ? <TractorSpinner size={15} /> : <ClipboardCheck size={15} />}<span>{evaluating ? 'Evaluating…' : 'Evaluate my plan'}</span></button>
    <button type="submit" className="button button-dark" disabled={disabled} aria-describedby={describedBy}>
      {suggesting ? <TractorSpinner size={15} /> : <Sprout size={15} />}<span>Suggest plans</span></button>
    <button type="button" className="button button-quiet button-icon" aria-label="Choose which plans to suggest" title="Choose plans"
      aria-haspopup="dialog" aria-expanded={customizing} onClick={onCustomize}><Settings size={15} /></button>
    {choiceLabel !== null && <span className="strategy-note strategy-choice-count">{choiceLabel}</span>}
  </>
}
