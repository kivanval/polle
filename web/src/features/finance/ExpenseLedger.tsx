import { useMemo, useState } from 'react'
import { CircleCheck, CircleDashed, Download, Plus, ReceiptText, Search } from 'lucide-react'
import { problemMessage, type Field } from '@/features/farms/api'
import { budgetCategoryColors, budgetCategoryLabels, type BudgetCategory } from '@/features/farms/resources'
import { expenseInput, useSaveExpense, type Expense, type Worker } from './api'
import { download, formatCents, formatQuantity, formatUnitPrice, sumUsd, toCsv } from './money'
import { TractorSpinner } from '@/shared/ui/Tractor'

const MONTHS = Array.from({ length: 12 }, (_, index) => new Intl.DateTimeFormat('en', { month: 'long', timeZone: 'UTC' }).format(Date.UTC(2026, index, 1)))
const dateLabel = (iso: string) => new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', timeZone: 'UTC' }).format(new Date(iso + 'T00:00:00Z'))

type Status = 'all' | 'paid' | 'unpaid'

/** Every expense of the year, filterable, with totals to the cent and a CSV export of exactly what is shown. */
export function ExpenseLedger({ expenses, year, farmName, showFarm, fields, workers, equipmentLabel, onAdd, onOpen }: {
  expenses: Expense[]; year: number; farmName: (id: string) => string; showFarm: boolean; fields: Field[]; workers: Worker[]
  equipmentLabel: (id: number) => string | null; onAdd: () => void; onOpen: (expense: Expense) => void
}) {
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState<BudgetCategory | ''>('')
  const [status, setStatus] = useState<Status>('all')
  const [month, setMonth] = useState(0)
  const fieldName = useMemo(() => new Map(fields.map(field => [field.id, field.name])), [fields])
  const workerName = useMemo(() => new Map(workers.map(worker => [worker.id, worker.fullName])), [workers])

  const needle = query.trim().toLowerCase()
  const shown = expenses.filter(expense => (!category || expense.category === category)
    && (status === 'all' || (status === 'paid') === expense.isPaid)
    // A whole-year cost belongs to every month of its year.
    && (!month || expense.allYear || Number(expense.spentOn.slice(5, 7)) === month)
    && (!needle || [expense.description, expense.supplier, expense.documentNumber, expense.notes,
      expense.fieldId && fieldName.get(expense.fieldId), expense.workerId && workerName.get(expense.workerId)]
      .some(value => typeof value === 'string' && value.toLowerCase().includes(needle))))
  const total = sumUsd(shown.map(expense => expense.amountUsd))
  const unpaid = shown.filter(expense => !expense.isPaid)
  const unpaidTotal = sumUsd(unpaid.map(expense => expense.amountUsd))
  const filtered = Boolean(needle || category || status !== 'all' || month)

  function exportCsv() {
    const rows = [['Date', 'Farm', 'Category', 'Description', 'Quantity', 'Unit', 'Unit price USD', 'Amount USD', 'Paid', 'Supplier', 'Document', 'Field', 'Worker', 'Machinery', 'Notes'],
      ...shown.map(expense => [expense.allYear ? `${expense.spentOn.slice(0, 4)} (all year)` : expense.spentOn, farmName(expense.farmId), budgetCategoryLabels[expense.category], expense.description, expense.quantity, expense.unit,
        expense.unitPriceUsd, expense.amountUsd.toFixed(2), expense.isPaid ? 'yes' : 'no', expense.supplier, expense.documentNumber,
        expense.fieldId ? fieldName.get(expense.fieldId) ?? '' : '', expense.workerId ? workerName.get(expense.workerId) ?? '' : '',
        expense.equipmentId ? equipmentLabel(expense.equipmentId) ?? '' : '', expense.notes])]
    rows.push(['', '', '', 'Total', '', '', '', total.toFixed(2), '', '', '', '', '', '', ''])
    download(`expenses-${year}${month ? '-' + String(month).padStart(2, '0') : ''}.csv`, toCsv(rows))
  }

  return <section className="finance-card" aria-labelledby="ledger-title">
    <header className="finance-card-head">
      <div><h2 id="ledger-title">Expense ledger</h2><p>Every purchase, bill and payment in {year}, to the cent. Click a line to edit it.</p></div>
      <div className="finance-actions">
        <button type="button" className="button button-quiet" onClick={exportCsv} disabled={shown.length === 0}><Download size={15} />Export CSV</button>
        <button type="button" className="button button-dark" onClick={onAdd}><Plus size={15} />Record expense</button>
      </div>
    </header>

    <div className="ledger-tools" role="search">
      <label className="catalog-search"><Search size={15} aria-hidden="true" /><span className="visually-hidden">Search expenses</span>
        <input type="search" value={query} placeholder="Search description, supplier, invoice…" onChange={event => setQuery(event.target.value)} /></label>
      <label><span className="visually-hidden">Category</span>
        <select value={category} onChange={event => setCategory(event.target.value as BudgetCategory | '')}>
          <option value="">All categories</option>
          {(Object.keys(budgetCategoryLabels) as BudgetCategory[]).map(key => <option key={key} value={key}>{budgetCategoryLabels[key]}</option>)}
        </select></label>
      <label><span className="visually-hidden">Month</span>
        <select value={month} onChange={event => setMonth(Number(event.target.value))}>
          <option value={0}>Whole year</option>
          {MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}
        </select></label>
      <div className="finance-segmented is-compact" role="group" aria-label="Payment status">
        {(['all', 'paid', 'unpaid'] as const).map(value => <button key={value} type="button" aria-pressed={status === value} onClick={() => setStatus(value)}>
          {value === 'all' ? 'All' : value === 'paid' ? 'Paid' : 'Unpaid'}</button>)}
      </div>
    </div>

    {shown.length === 0
      ? <div className="finance-empty"><ReceiptText size={26} strokeWidth={1.25} />
          {filtered ? <><p>No expenses match these filters.</p>
              <button type="button" className="text-button" onClick={() => { setQuery(''); setCategory(''); setStatus('all'); setMonth(0) }}>Clear filters</button></>
            : <><p>Nothing recorded for {year} yet. Record fuel, seed, repairs and pay as they happen, and this page compares them with the plan.</p>
              <button type="button" className="button button-dark" onClick={onAdd}><Plus size={15} />Record the first expense</button></>}
        </div>
      : <div className="catalog-grid-wrap">
          <table className="catalog-grid ledger-table">
            <thead><tr>
              <th scope="col">Date</th><th scope="col">Description</th><th scope="col">Category</th><th scope="col" className="is-numeric">Quantity × price</th>
              <th scope="col">For</th>{showFarm && <th scope="col">Farm</th>}<th scope="col" className="is-numeric">Amount</th><th scope="col">Status</th>
            </tr></thead>
            <tbody>{shown.map(expense => {
              const links = [expense.fieldId && fieldName.get(expense.fieldId), expense.workerId && workerName.get(expense.workerId),
                expense.equipmentId && equipmentLabel(expense.equipmentId)].filter((value): value is string => Boolean(value))
              return <tr key={expense.id} onClick={() => onOpen(expense)}>
                <td className="mono ledger-date">{expense.allYear ? 'All year' : dateLabel(expense.spentOn)}</td>
                <td><button type="button" className="ledger-open" onClick={event => { event.stopPropagation(); onOpen(expense) }}>{expense.description}</button>
                  {(expense.supplier || expense.documentNumber) && <span className="catalog-sub">{[expense.supplier, expense.documentNumber && `No. ${expense.documentNumber}`].filter(Boolean).join(' · ')}</span>}</td>
                <td><span className="ledger-category"><i style={{ background: budgetCategoryColors[expense.category] }} />{budgetCategoryLabels[expense.category]}</span></td>
                <td className="is-numeric is-muted">{expense.quantity !== null
                  ? <>{formatQuantity(expense.quantity)}{expense.unit ? ` ${expense.unit}` : ''}{expense.unitPriceUsd !== null && <> × {formatUnitPrice(expense.unitPriceUsd)}</>}</> : '—'}</td>
                <td className="ledger-links">{links.length ? links.map(link => <span key={link}>{link}</span>) : <span className="is-muted">Whole farm</span>}</td>
                {showFarm && <td className="is-muted">{farmName(expense.farmId)}</td>}
                <td className="is-numeric mono ledger-amount">{formatCents(expense.amountUsd)}</td>
                <td onClick={event => event.stopPropagation()}><PaidToggle expense={expense} /></td>
              </tr>
            })}</tbody>
            <tfoot><tr>
              <th scope="row" colSpan={showFarm ? 6 : 5}>{shown.length} {shown.length === 1 ? 'line' : 'lines'}{filtered ? ' shown' : ''}
                {unpaid.length > 0 && <span className="ledger-unpaid"> · {unpaid.length} unpaid, {formatCents(unpaidTotal)}</span>}</th>
              <td className="is-numeric mono ledger-amount">{formatCents(total)}</td><td />
            </tr></tfoot>
          </table>
        </div>}
  </section>
}

/** Marks a bill paid or unpaid in place. */
function PaidToggle({ expense }: { expense: Expense }) {
  const save = useSaveExpense()
  const [error, setError] = useState('')
  async function toggle() {
    setError('')
    try { await save.mutateAsync({ expenseId: expense.id, farmId: expense.farmId, input: { ...expenseInput(expense), isPaid: !expense.isPaid } }) }
    catch (reason) { setError(problemMessage(reason)) }
  }
  return <>
    <button type="button" className={`ledger-status ${expense.isPaid ? 'is-paid' : 'is-unpaid'}`} disabled={save.isPending} onClick={() => void toggle()}
      title={expense.isPaid ? 'Mark as not paid' : 'Mark as paid'} aria-label={`${expense.isPaid ? 'Paid' : 'Not paid'}: ${expense.description}. ${expense.isPaid ? 'Mark as not paid' : 'Mark as paid'}`}>
      {save.isPending ? <TractorSpinner size={13} /> : expense.isPaid ? <CircleCheck size={13} /> : <CircleDashed size={13} />}
      {expense.isPaid ? 'Paid' : 'Unpaid'}</button>
    {error && <span className="field-error" role="alert">{error}</span>}
  </>
}
