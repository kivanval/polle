import { useEffect, useState } from 'react'
import { HandCoins, PencilLine, Phone, Plus, UserRound, Users } from 'lucide-react'
import { WeekEditor } from '@/features/farms/OperatorSchedule'
import { useSaveWorkerSchedule, type Expense, type Worker } from './api'
import { formatCents, monthHours, payLabel, plannedPay, plannedPayYear, sumUsd, weekHours } from './money'

const MONTHS_SHORT = Array.from({ length: 12 }, (_, index) => new Intl.DateTimeFormat('en', { month: 'short', timeZone: 'UTC' }).format(Date.UTC(2026, index, 1)))
const MONTHS = Array.from({ length: 12 }, (_, index) => new Intl.DateTimeFormat('en', { month: 'long', timeZone: 'UTC' }).format(Date.UTC(2026, index, 1)))
const hoursLabel = (hours: number) => `${Number.isInteger(hours) ? hours : hours.toFixed(1)} h`

/** The people on the farms: who they are, what they cost, and each person's own week of shifts. */
export function WorkersPanel({ workers, expenses, year, month, showFarm, farmName, laborName, selectedId, onSelect, onAdd, onEdit, onRecordPay }: {
  workers: Worker[]; expenses: Expense[]; year: number; month: number; showFarm: boolean
  farmName: (id: string) => string; laborName: (id: number | null) => string | null
  selectedId: number | null; onSelect: (id: number | null) => void; onAdd: () => void; onEdit: (worker: Worker) => void
  onRecordPay: (worker: Worker, month: number) => void
}) {
  const [dirty, setDirty] = useState(false)
  const selected = workers.find(worker => worker.id === selectedId) ?? null
  const paid = (worker: Worker) => sumUsd(expenses.filter(expense => expense.workerId === worker.id).map(expense => expense.amountUsd))
  const active = workers.filter(worker => worker.isActive)
  const monthTotal = sumUsd(active.map(worker => plannedPay(worker, year, month)))
  const yearTotal = sumUsd(active.map(worker => plannedPayYear(worker, year)))
  const weekly = active.reduce((sum, worker) => sum + weekHours(worker.shifts, month), 0)
  const labourPaid = sumUsd(expenses.filter(expense => expense.category === 'labor').map(expense => expense.amountUsd))

  return <>
    <section className="finance-card" aria-labelledby="workers-title">
      <header className="finance-card-head">
        <div><h2 id="workers-title">Workers and payroll</h2>
          <p>Each person has their own pay terms and their own week of shifts. Planned pay comes from those shifts. Paid is what the ledger records for them.</p></div>
        <div className="finance-actions"><button type="button" className="button button-dark" onClick={onAdd}><Plus size={15} />Add worker</button></div>
      </header>

      <dl className="finance-mini">
        <div><dt>Active workers</dt><dd>{active.length}<small> of {workers.length}</small></dd></div>
        <div><dt>Scheduled, {MONTHS[month - 1]}</dt><dd>{hoursLabel(weekly)}<small> a week</small></dd></div>
        <div><dt>Planned pay, {MONTHS[month - 1]}</dt><dd className="mono">{formatCents(monthTotal)}</dd></div>
        <div><dt>Planned pay, {year}</dt><dd className="mono">{formatCents(yearTotal)}</dd></div>
        <div><dt>Labour paid, {year}</dt><dd className="mono">{formatCents(labourPaid)}</dd></div>
      </dl>

      {workers.length === 0
        ? <div className="finance-empty"><Users size={26} strokeWidth={1.25} />
            <p>No workers yet. Add the people who work on your farms, with their hourly rate or salary, then give each one their own shifts.</p>
            <button type="button" className="button button-dark" onClick={onAdd}><Plus size={15} />Add the first worker</button></div>
        : <div className="catalog-grid-wrap">
            <table className="catalog-grid workers-table">
              <thead><tr>
                <th scope="col">Worker</th>{showFarm && <th scope="col">Farm</th>}<th scope="col" className="is-numeric">Pay</th>
                <th scope="col" className="is-numeric">Week, {MONTHS_SHORT[month - 1]}</th><th scope="col" className="is-numeric">Planned, {MONTHS_SHORT[month - 1]}</th>
                <th scope="col" className="is-numeric">Paid in {year}</th>
              </tr></thead>
              <tbody>{workers.map(worker => {
                const locked = dirty
                return <tr key={worker.id} aria-selected={worker.id === selectedId} className={`${worker.isActive ? '' : 'is-inactive'} ${locked ? 'is-locked' : ''}`}
                  onClick={() => { if (!locked) onSelect(worker.id === selectedId ? null : worker.id) }}>
                  <td><button type="button" className="ledger-open" disabled={locked} title={locked ? 'Save or undo the shift changes first' : undefined}
                    aria-expanded={worker.id === selectedId} onClick={event => { event.stopPropagation(); if (!locked) onSelect(worker.id === selectedId ? null : worker.id) }}>{worker.fullName}</button>
                    <span className="catalog-sub">{[worker.position, laborName(worker.laborCategoryId), !worker.isActive && 'inactive'].filter(Boolean).join(' · ') || 'No job title'}</span></td>
                  {showFarm && <td className="is-muted">{farmName(worker.farmId)}</td>}
                  <td className="is-numeric mono">{payLabel(worker)}</td>
                  <td className="is-numeric mono">{weekHours(worker.shifts, month) ? hoursLabel(weekHours(worker.shifts, month)) : <span className="is-muted">no shifts</span>}</td>
                  <td className="is-numeric mono">{formatCents(plannedPay(worker, year, month))}</td>
                  <td className="is-numeric mono">{formatCents(paid(worker))}</td>
                </tr>
              })}</tbody>
            </table>
          </div>}
    </section>

    {selected && <WorkerDetail key={selected.id} worker={selected} year={year} month={month} payments={expenses.filter(expense => expense.workerId === selected.id)}
      farmName={showFarm ? farmName(selected.farmId) : null} laborName={laborName(selected.laborCategoryId)}
      onEdit={() => onEdit(selected)} onRecordPay={value => onRecordPay(selected, value)} onDirtyChange={setDirty} />}
  </>
}

function WorkerDetail({ worker, year, month, payments, farmName, laborName, onEdit, onRecordPay, onDirtyChange }: {
  worker: Worker; year: number; month: number; payments: Expense[]; farmName: string | null; laborName: string | null
  onEdit: () => void; onRecordPay: (month: number) => void; onDirtyChange: (dirty: boolean) => void
}) {
  const save = useSaveWorkerSchedule(worker.id)
  // Unsaved shifts lock the list; leaving this worker (or the tab) must release it.
  useEffect(() => () => onDirtyChange(false), [onDirtyChange])
  const months = Array.from({ length: 12 }, (_, index) => {
    const value = index + 1
    return { value, hours: monthHours(worker.shifts, year, value), planned: plannedPay(worker, year, value),
      paid: sumUsd(payments.filter(payment => Number(payment.spentOn.slice(5, 7)) === value).map(payment => payment.amountUsd)) }
  })
  const scale = Math.max(1, ...months.map(item => Math.max(item.planned, item.paid)))
  const plannedYear = sumUsd(months.map(item => item.planned)), paidYear = sumUsd(months.map(item => item.paid))

  return <section className="finance-card worker-detail" aria-labelledby="worker-detail-title">
    <header className="finance-card-head">
      <div className="worker-id"><span className="worker-avatar" aria-hidden="true"><UserRound size={20} /></span>
        <div><h2 id="worker-detail-title">{worker.fullName}</h2>
          <p>{[worker.position, laborName, farmName, payLabel(worker), worker.hiredOn && `hired ${worker.hiredOn}`].filter(Boolean).join(' · ')}</p>
          {worker.phone && <a className="worker-phone" href={`tel:${worker.phone}`}><Phone size={12} />{worker.phone}</a>}</div></div>
      <div className="finance-actions">
        <button type="button" className="button button-quiet" onClick={onEdit}><PencilLine size={15} />Edit details</button>
        <button type="button" className="button button-dark" onClick={() => onRecordPay(month)}><HandCoins size={15} />Record pay for {MONTHS[month - 1]}</button>
      </div>
    </header>
    {worker.notes && <p className="worker-notes">{worker.notes}</p>}

    <div className="worker-payroll">
      <h3>Pay by month, {year}</h3>
      <p>Planned {formatCents(plannedYear)} · paid {formatCents(paidYear)} · {paidYear > plannedYear ? `${formatCents(paidYear - plannedYear)} above plan` : `${formatCents(plannedYear - paidYear)} still to pay`}</p>
      <ol className="payroll-months">{months.map(item => <li key={item.value} className={item.value === month ? 'is-current' : ''}>
        <button type="button" onClick={() => onRecordPay(item.value)} aria-label={`${MONTHS[item.value - 1]}: planned ${formatCents(item.planned)}, paid ${formatCents(item.paid)}, ${hoursLabel(item.hours)} scheduled. Record pay`}
          title={`${MONTHS[item.value - 1]}: ${hoursLabel(item.hours)} scheduled\nPlanned ${formatCents(item.planned)}\nPaid ${formatCents(item.paid)}\nClick to record pay`}>
          <span className="payroll-bars" aria-hidden="true">
            <span className="is-planned" style={{ height: `${(item.planned / scale) * 100}%` }} />
            <span className="is-paid" style={{ height: `${(item.paid / scale) * 100}%` }} />
          </span>
          <span className="payroll-month">{MONTHS_SHORT[item.value - 1]}</span>
        </button></li>)}</ol>
      <p className="payroll-legend"><span><i className="is-planned" />Planned</span><span><i className="is-paid" />Paid</span></p>
    </div>

    <div className="worker-schedule">
      <h3>{worker.fullName}’s shifts</h3>
      <p>Only this person’s week. Every month keeps its own week, so harvest months can differ from winter.
        {worker.payType === 'hourly' ? ' Planned pay follows these hours.' : ' Their salary does not depend on these hours.'}</p>
      <WeekEditor label={worker.fullName} shifts={worker.shifts} readOnly={false} onDirtyChange={onDirtyChange} saving={save.isPending}
        onSave={async (chosen, week) => { await save.mutateAsync({ months: chosen, shifts: week }) }} />
    </div>

    {payments.length > 0 && <div className="worker-payments">
      <h3>Payments in {year}</h3>
      <ul>{payments.map(payment => <li key={payment.id}>
        <span className="mono">{payment.spentOn}</span><span>{payment.description}{!payment.isPaid && <em> · unpaid</em>}</span><span className="mono">{formatCents(payment.amountUsd)}</span>
      </li>)}</ul>
    </div>}
  </section>
}
