import { useState, type FormEvent } from 'react'
import { Check, Trash2 } from 'lucide-react'
import { problemMessage } from '@/features/farms/api'
import type { LaborCategory } from '@/features/farms/resources'
import { useDeleteWorker, useSaveWorker, type PayType, type Worker, fieldErrors } from './api'
import { Drawer } from './Drawer'
import { TractorSpinner } from '@/shared/ui/Tractor'

type Values = {
  farmId: string; fullName: string; position: string; laborCategoryId: string; phone: string
  payType: PayType; payRate: string; hiredOn: string; isActive: boolean; notes: string
}

/** Adds a worker or edits one's details and pay terms. Their shifts are edited on the page, not here. */
export function WorkerDrawer({ farms, farmId, worker, laborCategories, onClose, onSaved }: {
  farms: { id: string; name: string }[]; farmId: string | null; worker?: Worker; laborCategories: LaborCategory[]
  onClose: () => void; onSaved: (message: string, worker: Worker | null) => void
}) {
  const save = useSaveWorker()
  const remove = useDeleteWorker()
  const [values, setValues] = useState<Values>(() => ({
    farmId: worker?.farmId ?? farmId ?? (farms.length === 1 ? farms[0].id : ''), fullName: worker?.fullName ?? '', position: worker?.position ?? '',
    laborCategoryId: worker?.laborCategoryId ? String(worker.laborCategoryId) : '', phone: worker?.phone ?? '',
    payType: worker?.payType ?? 'hourly', payRate: worker ? String(worker.payRateUsd) : '', hiredOn: worker?.hiredOn ?? '',
    isActive: worker?.isActive ?? true, notes: worker?.notes ?? '',
  }))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState('')
  const [confirming, setConfirming] = useState(false)
  const busy = save.isPending || remove.isPending
  const set = <K extends keyof Values>(key: K, value: Values[K]) => { setValues(current => ({ ...current, [key]: value })); setErrors(current => ({ ...current, [key]: '' })) }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const next: Record<string, string> = {}
    if (!values.farmId) next.farmId = 'Choose the farm this person works on.'
    if (!values.fullName.trim()) next.fullName = 'Enter the worker’s name.'
    if (!/^\d+(\.\d{1,2})?$/.test(values.payRate.trim())) next.payRate = 'Enter the rate in USD with cents at most, e.g. 4.75.'
    setErrors(next); setFormError('')
    if (Object.keys(next).length) return
    try {
      const saved = await save.mutateAsync({
        workerId: worker?.id, farmId: values.farmId, input: {
          fullName: values.fullName.trim(), position: values.position.trim() || null, laborCategoryId: values.laborCategoryId ? Number(values.laborCategoryId) : null,
          phone: values.phone.trim() || null, payType: values.payType, payRateUsd: Number(values.payRate.trim()), hiredOn: values.hiredOn || null,
          isActive: values.isActive, notes: values.notes.trim() || null,
        },
      })
      onSaved(worker ? `Saved ${saved.fullName}.` : `Added ${saved.fullName}. Set their shifts below.`, saved)
    } catch (reason) {
      const map = fieldErrors(reason)
      if (map) setErrors(Object.fromEntries(Object.entries(map).map(([key, message]) => [key === 'payRateUsd' ? 'payRate' : key, message])))
      else setFormError(problemMessage(reason))
    }
  }

  async function confirmDelete() {
    if (!worker) return
    try { await remove.mutateAsync(worker.id); onSaved(`Removed ${worker.fullName}. Their payments stay in the ledger.`, null) }
    catch (reason) { setConfirming(false); setFormError(problemMessage(reason)) }
  }

  const field = (key: string) => ({ id: `worker-${key}`, disabled: busy, 'aria-invalid': Boolean(errors[key]), 'aria-describedby': errors[key] ? `worker-${key}-error` : undefined })
  const error = (key: string) => errors[key] ? <p id={`worker-${key}-error`} className="field-error" role="alert">{errors[key]}</p> : null

  return <Drawer kicker={worker ? 'Worker' : 'New worker'} title={worker ? worker.fullName : 'Add a worker'} busy={busy} onClose={onClose}
    footer={confirming
      ? <div className="catalog-confirm" role="alertdialog" aria-labelledby="worker-confirm-title">
          <p id="worker-confirm-title"><strong>Remove {worker?.fullName}?</strong> Their shifts are deleted. Payments already recorded stay in the ledger. To keep the history, mark them inactive instead.</p>
          <div className="field-actions">
            <button type="button" className="button button-danger" onClick={() => void confirmDelete()} disabled={busy}>
              {remove.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Remove worker</button>
            <button type="button" className="button button-quiet" autoFocus onClick={() => setConfirming(false)} disabled={busy}>Keep {worker?.fullName}</button>
          </div>
        </div>
      : <footer className="catalog-drawer-foot">
          <button type="submit" form="worker-form" className="button button-dark" disabled={busy}>
            {save.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}{worker ? 'Save worker' : 'Add worker'}</button>
          <button type="button" className="button button-quiet" onClick={onClose} disabled={busy}>Cancel</button>
          {worker && <button type="button" className="button button-quiet catalog-delete" onClick={() => setConfirming(true)} disabled={busy}><Trash2 size={15} />Remove…</button>}
        </footer>}>
    <form id="worker-form" className="catalog-form" onSubmit={submit} noValidate>
      {!worker && farms.length > 1 && <div className="catalog-field is-wide">
        <label htmlFor="worker-farmId">Farm</label>
        <select {...field('farmId')} value={values.farmId} onChange={event => set('farmId', event.target.value)}>
          <option value="" disabled>Choose a farm</option>
          {farms.map(option => <option key={option.id} value={option.id}>{option.name}</option>)}
        </select>{error('farmId')}</div>}
      <div className="catalog-field is-wide">
        <label htmlFor="worker-fullName">Full name</label>
        <input {...field('fullName')} value={values.fullName} maxLength={150} autoFocus autoComplete="off" onChange={event => set('fullName', event.target.value)} />{error('fullName')}</div>
      <div className="catalog-field">
        <label htmlFor="worker-position">Job title <span className="optional">optional</span></label>
        <input {...field('position')} value={values.position} maxLength={100} placeholder="e.g. Tractor driver" onChange={event => set('position', event.target.value)} />{error('position')}</div>
      <div className="catalog-field">
        <label htmlFor="worker-laborCategoryId">Labour type <span className="optional">optional</span></label>
        <select {...field('laborCategoryId')} value={values.laborCategoryId} onChange={event => set('laborCategoryId', event.target.value)}>
          <option value="">None</option>
          {laborCategories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
        </select>{error('laborCategoryId')}</div>
      <div className="catalog-field">
        <label htmlFor="worker-phone">Phone <span className="optional">optional</span></label>
        <input type="tel" {...field('phone')} value={values.phone} maxLength={40} onChange={event => set('phone', event.target.value)} />{error('phone')}</div>
      <div className="catalog-field">
        <label htmlFor="worker-hiredOn">Hired on <span className="optional">optional</span></label>
        <input type="date" {...field('hiredOn')} value={values.hiredOn} onChange={event => set('hiredOn', event.target.value)} />{error('hiredOn')}</div>

      <fieldset className="expense-calc">
        <legend>Pay</legend>
        <div className="catalog-field">
          <span className="finance-label" id="worker-payType-label">Paid</span>
          <div className="finance-segmented" role="radiogroup" aria-labelledby="worker-payType-label">
            {(['hourly', 'monthly'] as const).map(type => <label key={type}>
              <input type="radio" name="payType" value={type} checked={values.payType === type} disabled={busy} onChange={() => set('payType', type)} />
              {type === 'hourly' ? 'By the hour' : 'Monthly salary'}</label>)}
          </div></div>
        <div className="catalog-field">
          <label htmlFor="worker-payRate">{values.payType === 'hourly' ? 'USD per hour' : 'USD per month'}</label>
          <input inputMode="decimal" className="is-numeric" {...field('payRate')} value={values.payRate} placeholder={values.payType === 'hourly' ? '4.75' : '850.00'}
            onChange={event => set('payRate', event.target.value)} />{error('payRate')}</div>
        <p className="catalog-note">{values.payType === 'hourly'
          ? 'Planned pay is this rate times the hours of their shifts in each month.'
          : 'Planned pay is the full salary every month, whatever their shifts.'}</p>
      </fieldset>

      <div className="catalog-field is-wide">
        <label className="catalog-switch"><input type="checkbox" checked={values.isActive} disabled={busy} onChange={event => set('isActive', event.target.checked)} />
          <span>{values.isActive ? 'Active — counted in planned payroll' : 'Inactive — kept for history, no planned pay'}</span></label></div>
      <div className="catalog-field is-wide">
        <label htmlFor="worker-notes">Notes <span className="optional">optional</span></label>
        <textarea {...field('notes')} value={values.notes} maxLength={2000} onChange={event => set('notes', event.target.value)} />{error('notes')}</div>
    </form>
    {formError && <p className="form-error catalog-form-error" role="alert">{formError}</p>}
  </Drawer>
}
