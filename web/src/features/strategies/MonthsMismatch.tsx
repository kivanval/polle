import { AlertTriangle } from 'lucide-react'
import { monthText } from './format'

type Months = { startMonth: string; endMonth: string }

const span = (months: Months) => `${monthText(months.startMonth)} – ${monthText(months.endMonth)}`

/**
 * Why the suggested plans show no differences from the farmer's plan, in two lines. Either the current plan isn't
 * evaluated for its present crops yet (never evaluated, or changed since), or it was evaluated for other months than
 * the suggestions. Keyed by its text, so a new state remounts it instead of patching text a page translator may have
 * replaced.
 */
export function MonthsMismatch(props: { notEvaluated: true } | { notEvaluated?: false; plan: Months; strategies: Months }) {
  if (props.notEvaluated) return <div key="not-evaluated" className="strategy-mismatch" role="note">
    <AlertTriangle className="strategy-mismatch-icon" size={16} aria-hidden="true" />
    <div>
      <p><strong>Not compared yet: your current plan hasn’t been evaluated.</strong></p>
      <p><span>Press </span><b>Evaluate my plan</b><span> to compare it with these plans.</span></p>
    </div>
  </div>
  const { plan, strategies } = props
  return <div key={`${span(plan)}|${span(strategies)}`} className="strategy-mismatch" role="note">
    <AlertTriangle className="strategy-mismatch-icon" size={16} aria-hidden="true" />
    <div>
      <p><strong>Not compared: your plan and the suggested plans cover different months.</strong></p>
      <p><span>Your plan: </span><b>{span(plan)}</b><span> · Suggested plans: </span><b>{span(strategies)}</b><span>. Evaluate your plan for the same months to compare.</span></p>
    </div>
  </div>
}
