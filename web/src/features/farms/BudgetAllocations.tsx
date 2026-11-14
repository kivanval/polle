import { useState, type FormEvent } from 'react'
import { AlertTriangle, Check, PencilLine, X } from 'lucide-react'
import { problemMessage } from './api'
import {
  budgetCategoryColors, budgetCategoryLabels, formatUsd, useBudgetAllocations, useSetAllocation,
  type BudgetAllocation, type BudgetCategory,
} from './resources'
import { TractorSpinner } from '@/shared/ui/Tractor'

/**
 * The farm budget split into categories. The total stays the farm budget the planner reads; the split is planning
 * data and may run over, which is shown rather than blocked so a budget change does not erase the plan.
 */
export function BudgetAllocations({ farmId }: { farmId: string }) {
  const allocations = useBudgetAllocations(farmId)
  const [editing, setEditing] = useState<BudgetCategory | null>(null)
  if (allocations.isPending) return <div className="allocation-state" role="status"><TractorSpinner size={15} />Loading the breakdown…</div>
  if (allocations.error) return <div className="allocation-state"><p role="alert">{problemMessage(allocations.error)}</p>
    <button type="button" className="button button-quiet" onClick={() => void allocations.refetch()}>Try again</button></div>
  const { budgetUsd, allocatedUsd, unallocatedUsd, categories } = allocations.data
  const amount = (category: BudgetCategory) => allocations.data.allocations.find(item => item.category === category)?.amountUsd ?? 0
  const over = unallocatedUsd !== null && unallocatedUsd < 0

  return <div className="allocations">
    <dl className="allocation-figures">
      <div><dt>Allocated</dt><dd>{formatUsd(allocatedUsd)}</dd></div>
      <div className={over ? 'is-over' : ''}><dt>{over ? 'Over budget by' : 'Not yet allocated'}</dt>
        <dd>{unallocatedUsd === null ? '—' : formatUsd(Math.abs(unallocatedUsd))}</dd></div>
      <div><dt>Share planned</dt><dd>{budgetUsd ? `${Math.round((allocatedUsd / budgetUsd) * 100)}%` : '—'}</dd></div>
    </dl>
    <AllocationBar allocations={allocations.data.allocations} budgetUsd={budgetUsd} />
    {over && <p className="allocation-warning" role="status"><AlertTriangle size={15} />The categories add up to more than the farm budget. The planner still uses the farm budget of {formatUsd(budgetUsd!)}.</p>}
    {budgetUsd === null && <p className="allocation-hint">Set a farm budget above to see how much is left to plan. You can still record amounts now.</p>}
    <ul className="allocation-list" aria-label="Budget categories">
      {categories.map(category => <AllocationRow key={category} farmId={farmId} category={category} amount={amount(category)} budgetUsd={budgetUsd}
        editing={editing === category} locked={editing !== null && editing !== category}
        onEdit={() => setEditing(category)} onDone={() => setEditing(null)} />)}
    </ul>
  </div>
}

function AllocationRow({ farmId, category, amount, budgetUsd, editing, locked, onEdit, onDone }: {
  farmId: string; category: BudgetCategory; amount: number; budgetUsd: number | null
  editing: boolean; locked: boolean; onEdit: () => void; onDone: () => void
}) {
  const setAllocation = useSetAllocation(farmId)
  const [value, setValue] = useState('')
  const [error, setError] = useState('')
  const share = budgetUsd ? amount / budgetUsd : 0
  const label = budgetCategoryLabels[category]

  function start() { setValue(amount ? String(amount) : ''); setError(''); onEdit() }
  async function submit(event: FormEvent) {
    event.preventDefault()
    const cleaned = value.trim().replace(/[\s,]/g, '')
    if (cleaned && !/^\d+(\.\d{1,2})?$/.test(cleaned)) { setError('Enter an amount in USD, for example 12500 or 12500.50.'); return }
    const next = cleaned ? Number(cleaned) : null
    if (next !== null && next >= 1_000_000_000_000) { setError('Enter an amount below 1 trillion USD.'); return }
    if ((next ?? 0) === amount) { onDone(); return }
    try { await setAllocation.mutateAsync({ category, amountUsd: next }); onDone() }
    catch (reason) { setError(problemMessage(reason, 'amountUsd')) }
  }

  return <li className={`allocation-row ${editing ? 'is-editing' : ''} ${amount ? '' : 'is-zero'}`}>
    <span className="allocation-swatch" style={{ background: budgetCategoryColors[category] }} aria-hidden="true" />
    <span className="allocation-name">{label}</span>
    {editing
      ? <form className="allocation-form" onSubmit={submit} noValidate>
          <label className="money-input"><span aria-hidden="true">$</span>
            <span className="visually-hidden">{label}, USD</span>
            <input inputMode="decimal" autoComplete="off" autoFocus value={value} placeholder="0" disabled={setAllocation.isPending}
              aria-invalid={Boolean(error)} aria-describedby={error ? `allocation-error-${category}` : undefined}
              onChange={event => { setValue(event.target.value); setError('') }}
              onKeyDown={event => { if (event.key === 'Escape' && !setAllocation.isPending) onDone() }} /></label>
          <button type="submit" className="button button-dark button-icon" aria-label={`Save ${label}`} disabled={setAllocation.isPending}>
            {setAllocation.isPending ? <TractorSpinner size={14} /> : <Check size={14} />}</button>
          <button type="button" className="button button-quiet button-icon" aria-label="Cancel" disabled={setAllocation.isPending} onClick={onDone}><X size={14} /></button>
          {error && <p id={`allocation-error-${category}`} className="field-error" role="alert">{error}</p>}
        </form>
      : <button type="button" className="allocation-amount" disabled={locked} onClick={start} aria-label={`Edit ${label}: ${amount ? formatUsd(amount) : 'not planned'}`}>
          <span className="mono">{amount ? formatUsd(amount) : '—'}</span><PencilLine size={13} aria-hidden="true" /></button>}
    <span className="allocation-share mono">{budgetUsd && amount ? `${(share * 100).toFixed(share < 0.1 ? 1 : 0)}%` : ''}</span>
    <span className="allocation-meter" aria-hidden="true"><span style={{ width: `${Math.min(100, share * 100)}%`, background: budgetCategoryColors[category] }} /></span>
  </li>
}

/**
 * One bar for the whole budget: each category is a segment, the unallocated rest is left empty. When the plan runs
 * over, the bar is scaled to the plan and the budget line marks where the money ends.
 */
export function AllocationBar({ allocations, budgetUsd, compact = false }: { allocations: BudgetAllocation[]; budgetUsd: number | null; compact?: boolean }) {
  const allocated = allocations.reduce((sum, item) => sum + item.amountUsd, 0)
  const scale = Math.max(budgetUsd ?? 0, allocated)
  if (scale === 0) return <div className={`allocation-bar is-empty ${compact ? 'is-compact' : ''}`} role="img" aria-label="No budget or allocations yet" />
  const description = allocations.map(item => `${budgetCategoryLabels[item.category]} ${formatUsd(item.amountUsd)}`).join(', ')
  return <div className={`allocation-bar ${compact ? 'is-compact' : ''}`} role="img"
    aria-label={`${description || 'Nothing allocated'}${budgetUsd !== null ? ` of a ${formatUsd(budgetUsd)} budget` : ''}`}>
    {allocations.map(item => <span key={item.category} title={`${budgetCategoryLabels[item.category]}: ${formatUsd(item.amountUsd)}`}
      style={{ width: `${(item.amountUsd / scale) * 100}%`, background: budgetCategoryColors[item.category] }} />)}
    {budgetUsd !== null && allocated > budgetUsd && <i className="allocation-limit" style={{ left: `${(budgetUsd / scale) * 100}%` }} />}
  </div>
}
