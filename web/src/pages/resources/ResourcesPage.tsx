import { Fragment, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useQueries } from '@tanstack/react-query'
import { AlertTriangle, ArrowRight, ArrowUpRight, ChevronDown, ChevronRight, PencilLine, Plus, Tractor, Wallet } from 'lucide-react'
import { apiFetch } from '@/shared/api/client'
import { farmKeys, problemMessage, topLevelFields, type Field } from '@/features/farms/api'
import { formatHectares } from '@/features/farms/geo'
import { AllocationBar } from '@/features/farms/BudgetAllocations'
import {
  budgetCategoryColors, budgetCategoryLabels, farmResourcesPath, formatUsd, useEquipmentCategories, useLaborCategories, useResourceOverview,
  type BudgetAllocation, type BudgetCategory, type FarmResourceSummary, type FleetItem,
} from '@/features/farms/resources'
import { useExpenses, useWorkers, type Expense, type ExpenseInput, type Worker } from '@/features/finance/api'
import { formatCents, formatQuantity, formatUnitPrice, monthHours, plannedPay, plannedPayYear, sumUsd } from '@/features/finance/money'
import { MonthlyChart } from '@/features/finance/MonthlyChart'
import { ExpenseLedger } from '@/features/finance/ExpenseLedger'
import { ExpenseDrawer } from '@/features/finance/ExpenseDrawer'
import { WorkersPanel } from '@/features/finance/WorkersPanel'
import { WorkerDrawer } from '@/features/finance/WorkerDrawer'
import { BudgetSection, FleetSection } from '@/features/farms/FarmResourceEditors'
import { OperatorSchedule } from '@/features/farms/OperatorSchedule'
// Tables and the slide-over editor share the Expert data styles.
import '@/styles/catalog.css'
import { TractorLoader } from '@/shared/ui/Tractor'

const TABS = [
  { id: 'overview', label: 'Plan vs actual' },
  { id: 'expenses', label: 'Expenses' },
  { id: 'workers', label: 'Workers & shifts' },
  { id: 'machinery', label: 'Machinery' },
  { id: 'hours', label: 'Operator hours' },
] as const
type Tab = (typeof TABS)[number]['id']
const MONTHS = Array.from({ length: 12 }, (_, index) => new Intl.DateTimeFormat('en', { month: 'long', timeZone: 'UTC' }).format(Date.UTC(2026, index, 1)))

type Panel = { kind: 'expense'; expense?: Expense; prefill?: Partial<ExpenseInput> & { farmId?: string } } | { kind: 'worker'; worker?: Worker } | null

/** The farm business in one place: budget plan against money actually spent, the expense ledger, workers with their own shifts, and machinery. */
export function ResourcesPage() {
  const overview = useResourceOverview()
  const categories = useEquipmentCategories()
  const labor = useLaborCategories()
  const workers = useWorkers()
  const [params, setParams] = useSearchParams()
  const [now] = useState(() => new Date())
  const year = Number(params.get('year')) || now.getFullYear()
  const expenses = useExpenses(year)
  const fieldQueries = useQueries({
    queries: (overview.data?.farms ?? []).map(farm => ({ queryKey: farmKeys.fields(farm.id), queryFn: () => apiFetch<Field[]>(`/farms/${farm.id}/fields`) })),
  })
  const [panel, setPanel] = useState<Panel>(null)
  const [notice, setNotice] = useState('')

  const pending = [overview, categories, labor, workers, expenses].find(query => query.isPending)
  const failed = [overview, categories, labor, workers, expenses].find(query => query.error)
  if (failed) return <div className="atlas-state"><h1>Resources didn’t load.</h1>
    <p role="alert">{problemMessage(failed.error)}</p>
    <button className="button button-dark" onClick={() => { for (const query of [overview, categories, labor, workers, expenses]) void query.refetch() }}>Try again</button></div>
  if (pending || !overview.data || !categories.data || !labor.data || !workers.data || !expenses.data)
    return <div className="atlas-state" role="status"><TractorLoader /><p>Gathering your farms’ books…</p></div>

  const all = overview.data.farms
  if (all.length === 0) return <>
    <Heading />
    <section className="farms-empty"><img src="/images/contours.webp" alt="" />
      <div><h2>No farms yet.</h2><p>Budgets, expenses, workers and machinery belong to farms. Create a farm, then record what it spends and who works on it.</p>
        <Link className="button button-dark" to="/dashboard/farms/new"><Plus size={17} />Create farm</Link></div></section>
  </>

  const selectedId = params.get('farm')
  const selected = all.find(farm => farm.id === selectedId) ?? null
  const farms = selected ? [selected] : all
  const ids = new Set(farms.map(farm => farm.id))
  const tab: Tab = TABS.some(item => item.id === params.get('tab')) ? params.get('tab') as Tab : 'overview'
  const update = (changes: Record<string, string | null>) => setParams(current => {
    const next = new URLSearchParams(current)
    for (const [key, value] of Object.entries(changes)) { if (value === null) next.delete(key); else next.set(key, value) }
    return next
  }, { replace: true })

  const scopedExpenses = expenses.data.filter(expense => ids.has(expense.farmId))
  const scopedWorkers = workers.data.filter(worker => ids.has(worker.farmId))
  const allWorkers = workers.data
  const fields = fieldQueries.flatMap(query => query.data ?? [])
  const scopedFields = fields.filter(field => ids.has(field.farmId))
  const fleet = all.flatMap(farm => farm.fleet)
  const typeName = new Map(categories.data.map(category => [category.id, category.name]))
  const equipmentName = (item: FleetItem) => [typeName.get(item.equipmentCategoryId) ?? `Machinery type ${item.equipmentCategoryId}`, item.customName].filter(Boolean).join(' — ')
  const equipmentLabel = (id: number) => { const item = fleet.find(candidate => candidate.id === id); return item ? equipmentName(item) : null }
  const farmName = (id: string) => all.find(farm => farm.id === id)?.name ?? 'Removed farm'
  const laborName = (id: number | null) => (id === null ? null : labor.data.find(category => category.id === id)?.name ?? null)
  // Payroll figures for "this month": the current month in the current year, January in any other year.
  const month = year === now.getFullYear() ? now.getMonth() + 1 : 1

  function recordPay(worker: Worker, payMonth: number) {
    const hours = monthHours(worker.shifts, year, payMonth)
    const hourly = worker.payType === 'hourly' && hours > 0
    const lastDay = new Date(Date.UTC(year, payMonth, 0)).toISOString().slice(0, 10)
    setPanel({ kind: 'expense', prefill: {
      farmId: worker.farmId, category: 'labor', description: `Pay — ${worker.fullName}, ${MONTHS[payMonth - 1]} ${year}`, workerId: worker.id,
      spentOn: year === now.getFullYear() && payMonth === now.getMonth() + 1 ? now.toLocaleDateString('sv-SE') : lastDay, supplier: worker.fullName,
      ...(hourly ? { quantity: Math.round(hours * 1000) / 1000, unit: 'h', unitPriceUsd: worker.payRateUsd } : { amountUsd: plannedPay(worker, year, payMonth) || null }),
    } })
  }

  const done = (message: string) => { setPanel(null); setNotice(message) }
  const years = Array.from({ length: 7 }, (_, index) => now.getFullYear() + 1 - index)
  if (!years.includes(year)) years.push(year)

  return <div className="finance">
    <Heading />
    <div className="finance-bar">
      <div className="finance-filter" role="group" aria-label="Show farms">
        <button type="button" aria-pressed={!selected} onClick={() => update({ farm: null, worker: null })}>All farms <span className="mono">{all.length}</span></button>
        {all.map(farm => <button key={farm.id} type="button" aria-pressed={selected?.id === farm.id} onClick={() => update({ farm: farm.id, worker: null })}>{farm.name}</button>)}
      </div>
      <label className="finance-year"><span>Year</span>
        <select value={year} onChange={event => update({ year: event.target.value === String(now.getFullYear()) ? null : event.target.value })}>
          {years.sort((a, b) => b - a).map(value => <option key={value} value={value}>{value}</option>)}
        </select></label>
    </div>

    <Kpis farms={farms} expenses={scopedExpenses} workers={scopedWorkers} year={year} month={month} />

    <div className="finance-tabs" role="tablist" aria-label="Resources sections">
      {TABS.map(item => <button key={item.id} type="button" role="tab" id={`tab-${item.id}`} aria-selected={tab === item.id} aria-controls={`panel-${item.id}`}
        onClick={() => update({ tab: item.id === 'overview' ? null : item.id })}>{item.label}
        {item.id === 'expenses' && <span className="mono">{scopedExpenses.length}</span>}
        {item.id === 'workers' && <span className="mono">{scopedWorkers.length}</span>}</button>)}
    </div>
    {notice && <p className="catalog-notice finance-notice" role="status">{notice}</p>}

    <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} className="finance-panel">
      {tab === 'overview' && <>
        {selected && <BudgetSection farm={selected} />}
        <section className="finance-card" aria-labelledby="plan-actual">
          <header className="finance-card-head"><div><h2 id="plan-actual">Plan vs actual, {year}</h2>
            <p>The plan is each farm’s budget split by category. Actual is every expense recorded in {year}. Differences are exact to the cent.</p></div>
            <div className="finance-actions"><button type="button" className="button button-dark" onClick={() => setPanel({ kind: 'expense' })}><Plus size={15} />Record expense</button></div>
          </header>
          <PlanActual farms={farms} expenses={scopedExpenses} categories={overview.data.budgetCategories} onEdit={expense => setPanel({ kind: 'expense', expense })} />
        </section>
        <div className="finance-grid">
          <section className="finance-card" aria-labelledby="by-month">
            <header><h2 id="by-month">Spending by month</h2><p>All categories, paid and unpaid. Hover or focus a month for its breakdown.</p></header>
            <MonthlyChart expenses={scopedExpenses} year={year} />
          </section>
          <section className="finance-card" aria-labelledby="by-field">
            <header><h2 id="by-field">Cost by field</h2><p>Expenses linked to a field, per hectare. Unlinked costs belong to the whole farm.</p></header>
            <FieldCosts fields={scopedFields} expenses={scopedExpenses} farmName={selected ? null : farmName} loading={fieldQueries.some(query => query.isPending)} />
          </section>
        </div>
        <div className="finance-grid">
          <section className="finance-card" aria-labelledby="by-category">
            <header><h2 id="by-category">Planned budget by category</h2><p>{selected ? selected.name : 'All farms'}, planned spending per category.</p></header>
            <CategoryBreakdown farms={farms} categories={overview.data.budgetCategories} budget={farms.some(farm => farm.budgetUsd !== null) ? sumUsd(farms.map(farm => farm.budgetUsd ?? 0)) : null} />
          </section>
          <section className="finance-card" aria-labelledby="by-farm">
            <header><h2 id="by-farm">{selected ? 'This farm' : 'Budget by farm'}</h2><p>Bars show each farm’s planned split against its budget.</p></header>
            <ul className="finance-farms">{farms.map(farm => <FarmBudget key={farm.id} farm={farm} onFocus={selected ? undefined : () => update({ farm: farm.id })} />)}</ul>
          </section>
        </div>
      </>}

      {tab === 'expenses' && <ExpenseLedger expenses={scopedExpenses} year={year} farmName={farmName} showFarm={!selected} fields={fields} workers={allWorkers}
        equipmentLabel={equipmentLabel} onAdd={() => setPanel({ kind: 'expense' })} onOpen={expense => setPanel({ kind: 'expense', expense })} />}

      {tab === 'workers' && <WorkersPanel workers={scopedWorkers} expenses={scopedExpenses} year={year} month={month} showFarm={!selected}
        farmName={farmName} laborName={laborName} selectedId={Number(params.get('worker')) || null} onSelect={id => update({ worker: id === null ? null : String(id) })}
        onAdd={() => setPanel({ kind: 'worker' })} onEdit={worker => setPanel({ kind: 'worker', worker })} onRecordPay={recordPay} />}

      {tab === 'machinery' && <>
        {selected
          ? <FleetSection key={selected.id} farmId={selected.id} categories={categories.data} />
          : <section className="finance-card finance-machinery" aria-labelledby="machinery">
              <header><h2 id="machinery">Machinery</h2><p>Machinery types recorded on your farms. Choose a farm above to add or change its machines.</p></header>
              {farms.every(farm => farm.fleet.length === 0)
                ? <div className="fleet-empty"><Tractor size={28} strokeWidth={1.25} /><p>No machinery recorded yet. Choose a farm above to add it.</p></div>
                : <MachineryTable lines={farms.flatMap(farm => farm.fleet.map(item => ({ ...item, farm })))} names={typeName} />}
            </section>}
        <section className="finance-card finance-machinery" aria-labelledby="machine-costs">
          <header><h2 id="machine-costs">Cost by machine, {year}</h2><p>Fuel, repairs and other expenses linked to a machine.</p></header>
          <MachineCosts farms={farms} expenses={scopedExpenses} equipmentName={equipmentName} />
        </section>
      </>}

      {tab === 'hours' && (selected
        ? <OperatorSchedule key={selected.id} farmId={selected.id} />
        : <section className="finance-card" aria-labelledby="operator-hours">
            <header><h2 id="operator-hours">Operator working hours</h2>
              <p>Each farm keeps its own weekly time windows per labour type. They are not machine hours, which belong to each machinery line. Choose a farm to set them.</p></header>
            <ul className="finance-farm-picks">{all.map(farm => <li key={farm.id}>
              <button type="button" className="button button-quiet" onClick={() => update({ farm: farm.id, worker: null })}>{farm.name}<ArrowRight size={13} /></button></li>)}</ul>
          </section>)}
    </div>

    {panel?.kind === 'expense' && <ExpenseDrawer farms={all} farmId={selected?.id ?? null} expense={panel.expense} prefill={panel.prefill} fields={fields}
      workers={allWorkers} equipmentName={equipmentName} onClose={() => setPanel(null)} onSaved={done} />}
    {panel?.kind === 'worker' && <WorkerDrawer farms={all} farmId={selected?.id ?? null} worker={panel.worker} laborCategories={labor.data}
      onClose={() => setPanel(null)} onSaved={(message, worker) => {
        done(message)
        update({ tab: 'workers', worker: worker ? String(worker.id) : null, ...(worker && selected && worker.farmId !== selected.id ? { farm: null } : {}) })
      }} />}
  </div>
}

function Heading() {
  return <div className="workspace-heading"><span className="mono section-label">RESOURCES & FINANCE</span><h1>Your farm business, to the cent.</h1>
    <p>Plan the budget, record every expense as it happens, pay each worker for their own shifts, and see where the money goes by category, month, field and machine.
      Each farm’s total budget is what the crop planner uses.</p></div>
}

function Kpis({ farms, expenses, workers, year, month }: { farms: FarmResourceSummary[]; expenses: Expense[]; workers: Worker[]; year: number; month: number }) {
  const budgeted = farms.filter(farm => farm.budgetUsd !== null)
  const budget = sumUsd(budgeted.map(farm => farm.budgetUsd!))
  const spent = sumUsd(expenses.map(expense => expense.amountUsd))
  const budgetedIds = new Set(budgeted.map(farm => farm.id))
  // What is left is measured per farm, so one farm's overspend never hides behind another farm's spare money.
  const perFarm = budgeted.map(farm => farm.budgetUsd! - sumUsd(expenses.filter(expense => expense.farmId === farm.id).map(expense => expense.amountUsd)))
  const left = sumUsd(perFarm.filter(value => value > 0)), over = -sumUsd(perFarm.filter(value => value < 0))
  const unpaid = expenses.filter(expense => !expense.isPaid)
  const active = workers.filter(worker => worker.isActive)
  const area = farms.reduce((sum, farm) => sum + farm.areaHectares, 0)
  const unbudgeted = sumUsd(expenses.filter(expense => !budgetedIds.has(expense.farmId)).map(expense => expense.amountUsd))

  return <dl className="finance-kpis is-six">
    <div><dt>Total budget</dt><dd className="mono">{budgeted.length ? formatCents(budget) : '—'}</dd>
      <span>{budgeted.length === farms.length ? 'Used by the planner' : `${budgeted.length} of ${farms.length} farms have a budget`}</span></div>
    <div><dt>Spent in {year}</dt><dd className="mono">{formatCents(spent)}</dd>
      <span>{expenses.length} {expenses.length === 1 ? 'expense' : 'expenses'}{budget > 0 ? ` · ${((spent / budget) * 100).toFixed(1)}% of the budget` : ''}</span></div>
    <div className={over > 0 ? 'is-over' : ''}><dt>Budget left</dt><dd className="mono">{budgeted.length ? formatCents(left) : '—'}</dd>
      <span className={over > 0 ? 'is-over-note' : ''}>{over > 0 ? `Over budget by ${formatCents(over)}` : budgeted.length ? 'Every farm within budget' : 'Set a budget to track it'}
        {unbudgeted > 0 && budgeted.length > 0 ? ` · ${formatCents(unbudgeted)} on farms without a budget` : ''}</span></div>
    <div className={unpaid.length ? 'is-warn' : ''}><dt>Unpaid bills</dt><dd className="mono">{formatCents(sumUsd(unpaid.map(expense => expense.amountUsd)))}</dd>
      <span>{unpaid.length ? `${unpaid.length} ${unpaid.length === 1 ? 'bill' : 'bills'} to settle` : 'Everything is paid'}</span></div>
    <div><dt>Planned payroll, {MONTHS[month - 1]}</dt><dd className="mono">{formatCents(sumUsd(active.map(worker => plannedPay(worker, year, month))))}</dd>
      <span>{active.length} active {active.length === 1 ? 'worker' : 'workers'} · {formatCents(sumUsd(active.map(worker => plannedPayYear(worker, year))))} for {year}</span></div>
    <div><dt>Cost per hectare</dt><dd className="mono">{area > 0 ? formatCents(spent / area) : '—'}</dd>
      <span>{area > 0 ? `${formatHectares(area)} mapped` : 'Map fields to see it'}</span></div>
  </dl>
}

function PlanActual({ farms, expenses, categories, onEdit }: {
  farms: FarmResourceSummary[]; expenses: Expense[]; categories: BudgetCategory[]; onEdit: (expense: Expense) => void
}) {
  const [open, setOpen] = useState<BudgetCategory | null>(null)
  const rows = categories.map(category => {
    const planned = sumUsd(farms.map(farm => farm.allocations.find(item => item.category === category)?.amountUsd ?? 0))
    const items = expenses.filter(expense => expense.category === category)
    const spent = sumUsd(items.map(expense => expense.amountUsd))
    return { category, planned, spent, items, count: items.length, unpaid: sumUsd(items.filter(item => !item.isPaid).map(item => item.amountUsd)) }
  })
  const planned = sumUsd(rows.map(row => row.planned)), spent = sumUsd(rows.map(row => row.spent))
  if (planned === 0 && spent === 0) return <div className="finance-empty"><Wallet size={26} strokeWidth={1.25} />
    <p>Nothing planned or spent yet. Choose a farm above and split its budget into categories, then record expenses here as they happen.</p>
    {farms.length === 1 && <Link className="subtle-link" to={farmResourcesPath(farms[0].id)}>Plan {farms[0].name}’s budget <ArrowRight size={13} /></Link>}</div>
  return <div className="catalog-grid-wrap">
    <table className="catalog-grid plan-table">
      <thead><tr><th scope="col">Category</th><th scope="col" className="is-numeric">Planned</th><th scope="col" className="is-numeric">Spent</th>
        <th scope="col" className="is-numeric">Left</th><th scope="col">Used</th></tr></thead>
      <tbody>{rows.map(row => <Fragment key={row.category}>
        <PlanRow label={budgetCategoryLabels[row.category]} color={budgetCategoryColors[row.category]} planned={row.planned} spent={row.spent} count={row.count} unpaid={row.unpaid}
          expanded={row.count > 0 ? open === row.category : undefined} onToggle={() => setOpen(current => current === row.category ? null : row.category)}
          listId={`plan-items-${row.category}`} />
        {open === row.category && row.count > 0 && <tr className="plan-items" id={`plan-items-${row.category}`}><td colSpan={5}>
          <PlanExpenses expenses={row.items} label={budgetCategoryLabels[row.category]} onEdit={onEdit} /></td></tr>}
      </Fragment>)}</tbody>
      <tfoot><PlanRow label="Total" planned={planned} spent={spent} count={expenses.length} unpaid={sumUsd(rows.map(row => row.unpaid))} /></tfoot>
    </table>
  </div>
}

function PlanRow({ label, color, planned, spent, count, unpaid, expanded, onToggle, listId }: {
  label: string; color?: string; planned: number; spent: number; count: number; unpaid: number
  /** Set when the row has expenses to list: whether they are shown. */
  expanded?: boolean; onToggle?: () => void; listId?: string
}) {
  const left = Math.round((planned - spent) * 100) / 100
  const share = planned > 0 ? spent / planned : null
  const Cell = color ? 'td' : 'th'
  const summary = `${count} ${count === 1 ? 'expense' : 'expenses'}${unpaid > 0 ? ` · ${formatCents(unpaid)} unpaid` : ''}`
  return <tr className={left < 0 ? 'is-over' : ''}>
    <Cell scope={color ? undefined : 'row'}><span className="ledger-category">{color && <i style={{ background: color }} />}{label}</span>
      {expanded === undefined
        ? <span className="catalog-sub">{summary}</span>
        : <button type="button" className="plan-toggle catalog-sub" aria-expanded={expanded} aria-controls={expanded ? listId : undefined} onClick={onToggle}>
            {expanded ? <ChevronDown size={12} aria-hidden="true" /> : <ChevronRight size={12} aria-hidden="true" />}{summary}
            <span className="visually-hidden">{expanded ? `, hide ${label} expenses` : `, show and edit ${label} expenses`}</span></button>}</Cell>
    <td className="is-numeric mono">{planned > 0 ? formatCents(planned) : <span className="is-muted">not planned</span>}</td>
    <td className="is-numeric mono">{formatCents(spent)}</td>
    <td className="is-numeric mono plan-left">{planned > 0 || spent > 0 ? <>{left < 0 && <AlertTriangle size={12} aria-label="Over plan" />}{formatCents(left)}</> : '—'}</td>
    <td className="plan-used">{share === null
      ? <span className="is-muted">{spent > 0 ? 'unplanned' : '—'}</span>
      : <><span className="plan-meter" aria-hidden="true"><span style={{ width: `${Math.min(1, share) * 100}%`, background: color ?? 'var(--forest)' }} /></span>
          <small className="mono">{(share * 100).toFixed(1)}%</small></>}</td>
  </tr>
}

const expenseDate = new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', timeZone: 'UTC' })

/** The recorded expenses behind one Plan vs actual row. Edit opens the same expense, so a changed supplier or price updates it in place. */
function PlanExpenses({ expenses, label, onEdit }: { expenses: Expense[]; label: string; onEdit: (expense: Expense) => void }) {
  return <ul className="plan-expense-list" aria-label={`${label} expenses`}>{expenses.map(expense => <li key={expense.id}>
    <span className="mono plan-expense-date">{expense.allYear ? 'All year' : expenseDate.format(new Date(`${expense.spentOn}T00:00:00Z`))}</span>
    <span className="plan-expense-what">{expense.description}
      <small>{[expense.supplier, expense.quantity !== null
        ? `${formatQuantity(expense.quantity)}${expense.unit ? ` ${expense.unit}` : ''}${expense.unitPriceUsd !== null ? ` × ${formatUnitPrice(expense.unitPriceUsd)}` : ''}` : null]
        .filter(Boolean).join(' · ')}</small></span>
    <span className={`plan-expense-status ${expense.isPaid ? 'is-paid' : 'is-unpaid'}`}>{expense.isPaid ? 'Paid' : 'Unpaid'}</span>
    <span className="mono plan-expense-amount">{formatCents(expense.amountUsd)}</span>
    <button type="button" className="button button-quiet plan-expense-edit" onClick={() => onEdit(expense)} aria-label={`Edit ${expense.description}, ${formatCents(expense.amountUsd)}`}>
      <PencilLine size={14} />Edit</button>
  </li>)}</ul>
}

function FieldCosts({ fields, expenses, farmName, loading }: { fields: Field[]; expenses: Expense[]; farmName: ((id: string) => string) | null; loading: boolean }) {
  if (loading) return <p className="allocation-state" role="status"><TractorLoader compact />Loading fields…</p>
  const top = topLevelFields(fields)
  // A subfield's costs roll up into its parent, whose hectares already include it.
  const parentOf = new Map(fields.map(field => [field.id, field.parentFieldId ?? field.id]))
  const rows = top.map(field => {
    const spent = sumUsd(expenses.filter(expense => expense.fieldId && parentOf.get(expense.fieldId) === field.id).map(expense => expense.amountUsd))
    return { field, spent, perHectare: field.areaHectares > 0 ? spent / field.areaHectares : 0 }
  }).sort((a, b) => b.spent - a.spent || a.field.name.localeCompare(b.field.name))
  const farmWide = sumUsd(expenses.filter(expense => !expense.fieldId).map(expense => expense.amountUsd))
  if (top.length === 0) return <div className="finance-empty"><p>No fields mapped yet. Draw fields on a farm’s map to see costs per hectare.</p></div>
  return <table className="field-costs">
    <thead><tr><th scope="col">Field</th><th scope="col" className="is-numeric">Area</th><th scope="col" className="is-numeric">Spent</th><th scope="col" className="is-numeric">Per ha</th></tr></thead>
    <tbody>
      {rows.map(row => <tr key={row.field.id}><th scope="row">{row.field.name}{farmName && <small>{farmName(row.field.farmId)}</small>}</th>
        <td className="is-numeric mono">{formatHectares(row.field.areaHectares)}</td><td className="is-numeric mono">{formatCents(row.spent)}</td>
        <td className="is-numeric mono">{formatCents(row.perHectare)}</td></tr>)}
      <tr className="is-farm-wide"><th scope="row">Whole farm, no field</th><td /><td className="is-numeric mono">{formatCents(farmWide)}</td><td /></tr>
    </tbody>
  </table>
}

function MachineCosts({ farms, expenses, equipmentName }: { farms: FarmResourceSummary[]; expenses: Expense[]; equipmentName: (item: FleetItem) => string }) {
  const rows = farms.flatMap(farm => farm.fleet.map(item => {
    const items = expenses.filter(expense => expense.equipmentId === item.id)
    const by = (category: BudgetCategory) => sumUsd(items.filter(expense => expense.category === category).map(expense => expense.amountUsd))
    return { item, farm, count: items.length, fuel: by('fuel'), repairs: by('machinery'), total: sumUsd(items.map(expense => expense.amountUsd)) }
  })).sort((a, b) => b.total - a.total)
  if (rows.length === 0) return <div className="finance-empty"><p>No machinery to cost yet.</p></div>
  return <div className="catalog-grid-wrap"><table className="catalog-grid">
    <thead><tr><th scope="col">Machine</th>{farms.length > 1 && <th scope="col">Farm</th>}<th scope="col" className="is-numeric">Fuel</th>
      <th scope="col" className="is-numeric">Repairs</th><th scope="col" className="is-numeric">Total</th></tr></thead>
    <tbody>{rows.map(row => <tr key={row.item.id}><td>{equipmentName(row.item)}<span className="catalog-sub">{row.count} {row.count === 1 ? 'expense' : 'expenses'}</span></td>
      {farms.length > 1 && <td className="is-muted">{row.farm.name}</td>}
      <td className="is-numeric mono">{formatCents(row.fuel)}</td><td className="is-numeric mono">{formatCents(row.repairs)}</td>
      <td className="is-numeric mono">{formatCents(row.total)}</td></tr>)}</tbody>
  </table></div>
}

function CategoryBreakdown({ farms, categories, budget }: { farms: FarmResourceSummary[]; categories: BudgetCategory[]; budget: number | null }) {
  const totals = categories.map(category => ({
    category, amount: farms.reduce((sum, farm) => sum + (farm.allocations.find(item => item.category === category)?.amountUsd ?? 0), 0),
  })).filter(item => item.amount > 0).sort((a, b) => b.amount - a.amount)
  if (totals.length === 0) return <div className="finance-empty"><Wallet size={26} strokeWidth={1.25} />
    <p>No categories planned yet. Choose a farm above and split its budget into seeds, fuel, labour and more.</p>
    {farms.length === 1 && <Link className="subtle-link" to={farmResourcesPath(farms[0].id)}>Plan {farms[0].name}’s budget <ArrowRight size={13} /></Link>}</div>
  const scale = Math.max(budget ?? 0, totals.reduce((sum, item) => sum + item.amount, 0), ...totals.map(item => item.amount))
  const widest = totals[0].amount
  return <ul className="category-bars">
    {totals.map(item => <li key={item.category}>
      <span className="category-name"><i style={{ background: budgetCategoryColors[item.category] }} />{budgetCategoryLabels[item.category]}</span>
      <span className="category-track" aria-hidden="true"><span style={{ width: `${(item.amount / widest) * 100}%`, background: budgetCategoryColors[item.category] }} /></span>
      <span className="category-value mono">{formatUsd(item.amount)}<small>{budget ? `${Math.round((item.amount / scale) * 100)}%` : ''}</small></span>
    </li>)}
  </ul>
}

function FarmBudget({ farm, onFocus }: { farm: FarmResourceSummary; onFocus?: () => void }) {
  const over = farm.unallocatedUsd !== null && farm.unallocatedUsd < 0
  return <li className="finance-farm">
    <div className="finance-farm-head">
      {onFocus ? <button type="button" className="finance-farm-name" onClick={onFocus} title={`Show only ${farm.name}`}>{farm.name}</button>
        : <strong className="finance-farm-name">{farm.name}</strong>}
      <span className="mono">{farm.budgetUsd === null ? 'No budget' : formatUsd(farm.budgetUsd)}</span>
    </div>
    <AllocationBar allocations={farm.allocations} budgetUsd={farm.budgetUsd} compact />
    <div className="finance-farm-meta">
      <span className={over ? 'is-over' : ''}>{over && <AlertTriangle size={12} />}{farmBudgetLine(farm)}</span>
      <span>{farm.fieldCount} {farm.fieldCount === 1 ? 'field' : 'fields'}{farm.fieldCount ? ` · ${formatHectares(farm.areaHectares)}` : ''}</span>
    </div>
    {!onFocus && <AllocationLegend allocations={farm.allocations} />}
    {onFocus && <Link className="subtle-link" to={farmResourcesPath(farm.id)}>Edit budget, machinery and operator hours <ArrowUpRight size={13} /></Link>}
  </li>
}

function farmBudgetLine(farm: FarmResourceSummary) {
  if (farm.budgetUsd === null) return farm.allocatedUsd ? `${formatUsd(farm.allocatedUsd)} planned, no total budget` : 'Set a budget to plan spending'
  if (farm.unallocatedUsd! < 0) return `Over by ${formatUsd(-farm.unallocatedUsd!)}`
  return farm.allocatedUsd ? `${formatUsd(farm.unallocatedUsd!)} left to allocate` : 'Not split into categories yet'
}

function AllocationLegend({ allocations }: { allocations: BudgetAllocation[] }) {
  if (allocations.length === 0) return null
  return <ul className="allocation-legend">{allocations.map(item => <li key={item.category}>
    <i style={{ background: budgetCategoryColors[item.category] }} />{budgetCategoryLabels[item.category]}<span className="mono">{formatUsd(item.amountUsd)}</span></li>)}</ul>
}

type Line = FarmResourceSummary['fleet'][number] & { farm: FarmResourceSummary }

function MachineryTable({ lines, names }: { lines: Line[]; names: Map<number, string> }) {
  const byType = new Map<number, Line[]>()
  for (const line of lines) byType.set(line.equipmentCategoryId, [...(byType.get(line.equipmentCategoryId) ?? []), line])
  const groups = [...byType].map(([id, items]) => ({
    id, name: names.get(id) ?? `Machinery type ${id}`, items,
    units: items.reduce((sum, item) => sum + (item.unitsCount ?? 0), 0), unknown: items.filter(item => item.unitsCount === null).length,
  })).sort((a, b) => b.items.length - a.items.length || a.name.localeCompare(b.name))
  return <div className="machinery-table" role="table" aria-label="Machinery by type">
    <div role="row" className="machinery-row is-head"><span role="columnheader">Type</span><span role="columnheader">Units</span><span role="columnheader">On farms</span></div>
    {groups.map(group => <div key={group.id} role="row" className="machinery-row">
      <span role="cell" className="machinery-type"><strong>{group.name}</strong>
        <small>{group.items.length} {group.items.length === 1 ? 'line' : 'lines'}{group.items.some(item => item.customName) ? ` · ${group.items.flatMap(item => item.customName ?? []).join(', ')}` : ''}</small></span>
      <span role="cell" className="mono">{group.units || '—'}{group.unknown > 0 && <small>{group.unknown} not counted</small>}</span>
      <span role="cell" className="machinery-farms">{[...new Map(group.items.map(item => [item.farm.id, item.farm])).values()].map(farm =>
        <Link key={farm.id} to={farmResourcesPath(farm.id, 'machinery')}>{farm.name}</Link>)}</span>
    </div>)}
  </div>
}
