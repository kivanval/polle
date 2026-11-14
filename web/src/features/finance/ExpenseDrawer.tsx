import { useState, type FormEvent } from 'react'
import { Check, Trash2 } from 'lucide-react'
import { problemMessage, type Field } from '@/features/farms/api'
import { budgetCategoryLabels, type BudgetCategory, type FleetItem } from '@/features/farms/resources'
import { useDeleteExpense, useSaveExpense, type Expense, type ExpenseInput, type Worker, fieldErrors } from './api'
import { Drawer } from './Drawer'
import { formatCents, lineAmount } from './money'
import { TractorSpinner } from '@/shared/ui/Tractor'

export type FarmOption = { id: string; name: string; fleet: FleetItem[] }

type Values = {
  farmId: string; spentOn: string; allYear: boolean; category: BudgetCategory | ''; description: string; quantity: string; unit: string; unitPrice: string
  amount: string; supplier: string; documentNumber: string; isPaid: boolean; fieldId: string; workerId: string; equipmentId: string; notes: string
}

const UNITS = ['l', 'kg', 't', 'ha', 'h', 'pcs', 'bag', 'm³', 'km', 'month']
const text = (value: number | string | null | undefined) => (value === null || value === undefined ? '' : String(value))
const today = () => new Date().toLocaleDateString('sv-SE')
/** Years a whole-year cost can name: ten back to five ahead of this year, and the one already chosen. */
const yearChoices = (chosen: number) => {
  const now = new Date().getFullYear()
  const years = Array.from({ length: 16 }, (_, index) => now - 10 + index)
  return Number.isFinite(chosen) && !years.includes(chosen) ? [...years, chosen].sort((a, b) => a - b) : years
}
const decimal = (places: number) => new RegExp(`^\\d+(\\.\\d{1,${places}})?$`)

/** Adds or edits one ledger line. `prefill` starts a new line, e.g. a worker's pay. */
export function ExpenseDrawer({ farms, farmId, expense, prefill, fields, workers, equipmentName, onClose, onSaved }: {
  farms: FarmOption[]; farmId: string | null; expense?: Expense; prefill?: Partial<ExpenseInput> & { farmId?: string }
  fields: Field[]; workers: Worker[]; equipmentName: (item: FleetItem) => string
  onClose: () => void; onSaved: (message: string) => void
}) {
  const save = useSaveExpense()
  const remove = useDeleteExpense()
  const start = expense ?? prefill
  const [values, setValues] = useState<Values>(() => ({
    farmId: expense?.farmId ?? prefill?.farmId ?? farmId ?? (farms.length === 1 ? farms[0].id : ''),
    spentOn: start?.spentOn ?? today(), allYear: start?.allYear ?? false, category: start?.category ?? '', description: text(start?.description),
    quantity: text(start?.quantity), unit: text(start?.unit), unitPrice: text(start?.unitPriceUsd),
    amount: start?.quantity != null && start?.unitPriceUsd != null ? '' : text(start?.amountUsd),
    supplier: text(start?.supplier), documentNumber: text(start?.documentNumber), isPaid: start?.isPaid ?? true,
    fieldId: text(start?.fieldId), workerId: text(start?.workerId), equipmentId: text(start?.equipmentId), notes: text(start?.notes),
  }))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState('')
  const [confirming, setConfirming] = useState(false)
  const busy = save.isPending || remove.isPending

  const set = <K extends keyof Values>(key: K, value: Values[K]) => {
    setValues(current => ({ ...current, [key]: value, ...(key === 'farmId' ? { fieldId: '', workerId: '', equipmentId: '' } : {}) }))
    setErrors(current => ({ ...current, [key]: '', ...(key === 'quantity' || key === 'unitPrice' ? { amount: '' } : {}) }))
  }
  const farm = farms.find(option => option.id === values.farmId)
  const farmFields = fields.filter(field => field.farmId === values.farmId)
  const farmWorkers = workers.filter(worker => worker.farmId === values.farmId)
  const qty = values.quantity.trim(), unitPrice = values.unitPrice.trim()
  const computed = decimal(3).test(qty) && decimal(4).test(unitPrice) && Number(qty) > 0 ? lineAmount(Number(qty), Number(unitPrice)) : null

  function validate() {
    const next: Record<string, string> = {}
    if (!farm) next.farmId = 'Choose the farm that spent the money.'
    if (!/^\d{4}-\d{2}-\d{2}$/.test(values.spentOn)) next.spentOn = values.allYear ? 'Choose the year the cost covers.' : 'Enter the date of the expense.'
    if (!values.category) next.category = 'Choose a category.'
    if (!values.description.trim()) next.description = 'Describe what was bought or paid.'
    if (qty && (!decimal(3).test(qty) || Number(qty) <= 0)) next.quantity = 'Use a number above 0 with up to 3 decimals, e.g. 1250.5.'
    if (unitPrice && !decimal(4).test(unitPrice)) next.unitPrice = 'Use USD with up to 4 decimals, e.g. 1.0525.'
    if (computed === null) {
      const amount = values.amount.trim()
      if (!amount) next.amount = qty || unitPrice ? 'Enter both quantity and unit price, or the total.' : 'Enter the total.'
      else if (!decimal(2).test(amount)) next.amount = 'Use USD with cents at most and no separators, e.g. 1204.50.'
    }
    return next
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const next = validate()
    setErrors(next); setFormError('')
    if (Object.keys(next).length) return
    const input: ExpenseInput = {
      spentOn: values.spentOn, allYear: values.allYear, category: values.category as BudgetCategory, description: values.description.trim(),
      quantity: qty ? Number(qty) : null, unit: values.unit.trim() || null, unitPriceUsd: unitPrice ? Number(unitPrice) : null,
      amountUsd: computed ?? Number(values.amount), supplier: values.supplier.trim() || null, documentNumber: values.documentNumber.trim() || null,
      isPaid: values.isPaid, fieldId: values.fieldId || null, workerId: values.workerId ? Number(values.workerId) : null,
      equipmentId: values.equipmentId ? Number(values.equipmentId) : null, notes: values.notes.trim() || null,
    }
    try {
      const saved = await save.mutateAsync({ expenseId: expense?.id, farmId: values.farmId, input })
      onSaved(`${expense ? 'Saved' : 'Recorded'} ${saved.description}, ${formatCents(saved.amountUsd)}.`)
    } catch (reason) {
      const map = fieldErrors(reason)
      if (map) setErrors(Object.fromEntries(Object.entries(map).map(([key, message]) => [key === 'amountUsd' ? 'amount' : key === 'unitPriceUsd' ? 'unitPrice' : key, message])))
      else setFormError(problemMessage(reason))
    }
  }

  async function confirmDelete() {
    if (!expense) return
    try { await remove.mutateAsync(expense.id); onSaved(`Removed ${expense.description}.`) } catch (reason) { setConfirming(false); setFormError(problemMessage(reason)) }
  }

  const field = (key: string) => ({ id: `expense-${key}`, disabled: busy, 'aria-invalid': Boolean(errors[key]), 'aria-describedby': errors[key] ? `expense-${key}-error` : undefined })
  const error = (key: string) => errors[key] ? <p id={`expense-${key}-error`} className="field-error" role="alert">{errors[key]}</p> : null

  return <Drawer kicker={expense ? 'Expense' : 'New expense'} title={expense ? expense.description : 'Record an expense'} busy={busy} onClose={onClose}
    footer={confirming
      ? <div className="catalog-confirm" role="alertdialog" aria-labelledby="expense-confirm-title">
          <p id="expense-confirm-title"><strong>Remove {expense?.description}, {formatCents(expense?.amountUsd ?? 0)}?</strong> It leaves every total on this page.</p>
          <div className="field-actions">
            <button type="button" className="button button-danger" onClick={() => void confirmDelete()} disabled={busy}>
              {remove.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Remove expense</button>
            <button type="button" className="button button-quiet" autoFocus onClick={() => setConfirming(false)} disabled={busy}>Keep it</button>
          </div>
        </div>
      : <footer className="catalog-drawer-foot">
          <button type="submit" form="expense-form" className="button button-dark" disabled={busy}>
            {save.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}{expense ? 'Save expense' : 'Record expense'}</button>
          <button type="button" className="button button-quiet" onClick={onClose} disabled={busy}>Cancel</button>
          {expense && <button type="button" className="button button-quiet catalog-delete" onClick={() => setConfirming(true)} disabled={busy}><Trash2 size={15} />Remove…</button>}
        </footer>}>
    <form id="expense-form" className="catalog-form" onSubmit={submit} noValidate>
      {!expense && farms.length > 1 && <div className="catalog-field is-wide">
        <label htmlFor="expense-farmId">Farm</label>
        <select {...field('farmId')} value={values.farmId} onChange={event => set('farmId', event.target.value)} autoFocus={!values.farmId}>
          <option value="" disabled>Choose a farm</option>
          {farms.map(option => <option key={option.id} value={option.id}>{option.name}</option>)}
        </select>{error('farmId')}</div>}
      <div className="catalog-field">
        <label htmlFor="expense-spentOn">{values.allYear ? 'Year' : 'Date'}</label>
        {/* A whole-year cost names only its year; it is stored on January 1. */}
        {values.allYear
          ? <select {...field('spentOn')} value={values.spentOn.slice(0, 4)} onChange={event => set('spentOn', `${event.target.value}-01-01`)}>
              {yearChoices(Number(values.spentOn.slice(0, 4))).map(year => <option key={year} value={year}>{year}</option>)}
            </select>
          : <input type="date" {...field('spentOn')} value={values.spentOn} onChange={event => set('spentOn', event.target.value)} />}
        <label className="catalog-switch expense-all-year"><input type="checkbox" checked={values.allYear} disabled={busy}
          onChange={event => { const all = event.target.checked; setValues(current => ({ ...current, allYear: all,
            spentOn: all ? `${(current.spentOn || today()).slice(0, 4)}-01-01` : current.spentOn.slice(0, 4) === today().slice(0, 4) ? today() : current.spentOn })) }} />
          <span>All year: a lease, insurance or another yearly cost</span></label>
        {error('spentOn')}</div>
      <div className="catalog-field">
        <label htmlFor="expense-category">Category</label>
        <select {...field('category')} value={values.category} onChange={event => set('category', event.target.value as BudgetCategory)}>
          <option value="" disabled>Choose a category</option>
          {(Object.keys(budgetCategoryLabels) as BudgetCategory[]).map(key => <option key={key} value={key}>{budgetCategoryLabels[key]}</option>)}
        </select>{error('category')}</div>
      <div className="catalog-field is-wide">
        <label htmlFor="expense-description">What was bought or paid</label>
        <input {...field('description')} value={values.description} maxLength={200} placeholder="e.g. Diesel for spring sowing"
          autoFocus={Boolean(values.farmId)} onChange={event => set('description', event.target.value)} />{error('description')}</div>

      <fieldset className="expense-calc">
        <legend>Amount</legend>
        <div className="catalog-field">
          <label htmlFor="expense-quantity">Quantity <span className="optional">optional</span></label>
          <input inputMode="decimal" className="is-numeric" {...field('quantity')} value={values.quantity} placeholder="1250" onChange={event => set('quantity', event.target.value)} />{error('quantity')}</div>
        <div className="catalog-field">
          <label htmlFor="expense-unit">Unit <span className="optional">optional</span></label>
          <input {...field('unit')} list="expense-units" value={values.unit} maxLength={20} placeholder="l" onChange={event => set('unit', event.target.value)} />
          <datalist id="expense-units">{UNITS.map(unit => <option key={unit} value={unit} />)}</datalist>{error('unit')}</div>
        <div className="catalog-field">
          <label htmlFor="expense-unitPrice">Price per unit, USD <span className="optional">optional</span></label>
          <input inputMode="decimal" className="is-numeric" {...field('unitPrice')} value={values.unitPrice} placeholder="1.0525" onChange={event => set('unitPrice', event.target.value)} />{error('unitPrice')}</div>
        <div className="catalog-field">
          <label htmlFor="expense-amount">Total, USD</label>
          {computed !== null
            ? <output id="expense-amount" className="expense-total" aria-live="polite">{formatCents(computed)}<small>{qty} × {unitPrice}</small></output>
            : <input inputMode="decimal" className="is-numeric" {...field('amount')} value={values.amount} placeholder="1204.50" onChange={event => set('amount', event.target.value)} />}
          {error('amount')}</div>
      </fieldset>

      <div className="catalog-field">
        <label htmlFor="expense-supplier">Supplier or payee <span className="optional">optional</span></label>
        <input {...field('supplier')} value={values.supplier} maxLength={150} onChange={event => set('supplier', event.target.value)} />{error('supplier')}</div>
      <div className="catalog-field">
        <label htmlFor="expense-documentNumber">Invoice or receipt no. <span className="optional">optional</span></label>
        <input {...field('documentNumber')} value={values.documentNumber} maxLength={60} onChange={event => set('documentNumber', event.target.value)} />{error('documentNumber')}</div>
      <div className="catalog-field is-wide">
        <label className="catalog-switch"><input type="checkbox" checked={values.isPaid} disabled={busy} onChange={event => set('isPaid', event.target.checked)} />
          <span>{values.isPaid ? 'Paid' : 'Not paid yet — a bill to settle'}</span></label></div>

      <div className="catalog-field">
        <label htmlFor="expense-fieldId">Field <span className="optional">optional</span></label>
        <select {...field('fieldId')} value={values.fieldId} disabled={busy || !farm} onChange={event => set('fieldId', event.target.value)}>
          <option value="">Whole farm</option>
          {farmFields.map(option => <option key={option.id} value={option.id}>{option.parentFieldId ? '— ' : ''}{option.name}</option>)}
        </select>{error('fieldId')}</div>
      <div className="catalog-field">
        <label htmlFor="expense-workerId">Worker <span className="optional">optional</span></label>
        <select {...field('workerId')} value={values.workerId} disabled={busy || !farm} onChange={event => set('workerId', event.target.value)}>
          <option value="">Nobody</option>
          {farmWorkers.map(option => <option key={option.id} value={option.id}>{option.fullName}</option>)}
        </select>{error('workerId')}</div>
      <div className="catalog-field is-wide">
        <label htmlFor="expense-equipmentId">Machinery <span className="optional">optional</span></label>
        <select {...field('equipmentId')} value={values.equipmentId} disabled={busy || !farm} onChange={event => set('equipmentId', event.target.value)}>
          <option value="">None</option>
          {farm?.fleet.map(item => <option key={item.id} value={item.id}>{equipmentName(item)}</option>)}
        </select>{error('equipmentId')}</div>
      <div className="catalog-field is-wide">
        <label htmlFor="expense-notes">Notes <span className="optional">optional</span></label>
        <textarea {...field('notes')} value={values.notes} maxLength={2000} onChange={event => set('notes', event.target.value)} />{error('notes')}</div>
    </form>
    {formError && <p className="form-error catalog-form-error" role="alert">{formError}</p>}
  </Drawer>
}
