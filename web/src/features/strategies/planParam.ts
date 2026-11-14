import { useSearchParams } from 'react-router'

/** URL parameter holding the field whose "Plan & strategies" view is open, so a reload (F5) reopens it. */
export const PLAN_PARAM = 'plan'
/** URL parameter holding a saved strategy whose Details open inside that view. */
export const STRATEGY_PARAM = 'strategy'

export function usePlanParam() {
  const [params, setParams] = useSearchParams()
  const planning = params.get(PLAN_PARAM)
  const strategy = params.get(STRATEGY_PARAM)
  /** Opens a field's view, optionally on one saved strategy; null closes it. */
  const setPlanning = (fieldId: string | null, strategyId?: string) => setParams(current => {
    const next = new URLSearchParams(current)
    if (fieldId) next.set(PLAN_PARAM, fieldId); else next.delete(PLAN_PARAM)
    if (fieldId && strategyId) next.set(STRATEGY_PARAM, strategyId); else next.delete(STRATEGY_PARAM)
    return next
  }, { replace: true })
  return [planning, setPlanning, strategy] as const
}
