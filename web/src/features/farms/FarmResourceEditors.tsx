import { useRef, useState, type FormEvent } from 'react'
import { Check, PencilLine, Plus, Tractor, Trash2 } from 'lucide-react'
import { problemMessage, type Farm } from '@/features/farms/api'
import { formatUsd, useDeleteFleetItem, useFleet, useSaveFleetItem, useUpdateBudget,
  type EquipmentCategory, type FleetItem } from '@/features/farms/resources'
import { BudgetAllocations } from '@/features/farms/BudgetAllocations'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'

/** One farm's budget total and its split by category. */
export function BudgetSection({ farm }: { farm: Pick<Farm, 'id' | 'budgetUsd'> }) {
  const updateBudget = useUpdateBudget(farm.id)
  const [editing, setEditing] = useState(false)
  const [value, setValue] = useState('')
  const [error, setError] = useState('')

  function edit() { setValue(farm.budgetUsd === null ? '' : String(farm.budgetUsd)); setError(''); setEditing(true) }
  async function save(budget: number | null) {
    try { await updateBudget.mutateAsync(budget); setEditing(false) } catch (reason) { setError(problemMessage(reason, 'budgetUsd')) }
  }
  function submit(event: FormEvent) {
    event.preventDefault()
    const cleaned = value.trim()
    if (!cleaned) { void save(null); return }
    const amount = Number(cleaned)
    if (!Number.isFinite(amount) || amount < 0 || amount >= 1_000_000_000_000 || !/^\d+(\.\d{1,2})?$/.test(cleaned)) { setError('Enter USD below 1 trillion, using a decimal point and no separators, for example 120000.50.'); return }
    void save(amount)
  }

  return <section className="resource-section" aria-labelledby="budget-title">
    <header className="resource-head"><div><h2 id="budget-title">Budget</h2>
      <p>One total for the farm, in US dollars, the currency of the planner’s cost tables.</p></div></header>
    {editing
      ? <form className="budget-form" onSubmit={submit} noValidate>
          <label htmlFor="budget">Budget, USD</label>
          <div className="money-input"><span aria-hidden="true">$</span>
            <input id="budget" inputMode="decimal" autoComplete="off" value={value} autoFocus disabled={updateBudget.isPending} placeholder="120000"
              aria-invalid={Boolean(error)} aria-describedby={error ? 'budget-error' : undefined}
              onChange={event => { setValue(event.target.value); setError('') }} onKeyDown={event => { if (event.key === 'Escape') setEditing(false) }} /></div>
          {error && <p id="budget-error" className="field-error" role="alert">{error}</p>}
          <div className="field-actions">
            <button type="submit" className="button button-dark" disabled={updateBudget.isPending}>
              {updateBudget.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}Save budget</button>
            <button type="button" className="button button-quiet" onClick={() => setEditing(false)} disabled={updateBudget.isPending}>Cancel</button>
            {farm.budgetUsd !== null && <button type="button" className="text-button" onClick={() => void save(null)} disabled={updateBudget.isPending}>Clear budget</button>}
          </div>
        </form>
      : <div className="budget-value">
          {farm.budgetUsd === null ? <span className="resource-empty">Not set yet</span> : <strong>{formatUsd(farm.budgetUsd)}</strong>}
          <button type="button" className="button button-quiet" onClick={edit}><PencilLine size={15} />{farm.budgetUsd === null ? 'Set budget' : 'Edit budget'}</button>
        </div>}
    <div className="allocation-head"><h3>Where the money goes</h3>
      <p>Split the budget into categories to plan spending. The planner keeps using the total above.</p></div>
    <BudgetAllocations farmId={farm.id} />
  </section>
}

/** One farm's machinery lines. Machine hours are a property of each line, separate from people's working hours. */
export function FleetSection({ farmId, categories }: { farmId: string; categories: EquipmentCategory[] }) {
  const query = useFleet(farmId)
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const addButton = useRef<HTMLButtonElement>(null)
  const closeForm = () => { setEditing(null); requestAnimationFrame(() => addButton.current?.focus()) }
  const category = (id: number) => categories.find(candidate => candidate.id === id)
  const head = <header className="resource-head">
    <div><h2 id="fleet-title">Machinery</h2><p>Record the kinds of equipment available on this farm. A listed type does not establish how many machines can work at once.
      Machine hours are each line’s monthly operating limit per unit. They are not operator working hours.</p></div>
  </header>
  if (query.isPending) return <section className="resource-section" aria-labelledby="fleet-title">{head}<p className="allocation-state" role="status"><TractorLoader compact />Loading machinery…</p></section>
  if (query.error) return <section className="resource-section" aria-labelledby="fleet-title">{head}<p className="field-error" role="alert">{problemMessage(query.error)}</p>
    <button type="button" className="button button-quiet" onClick={() => void query.refetch()}>Try again</button></section>
  const fleet = query.data

  return <section className="resource-section" aria-labelledby="fleet-title">
    {head}
    {fleet.length === 0 && editing !== 'new' && <div className="fleet-empty"><Tractor size={28} strokeWidth={1.25} />
      <p>No machinery yet. Add a type such as tractor, combine or sprayer.</p></div>}
    {fleet.length > 0 && <ul className="fleet-list" aria-label="Machinery">
      {fleet.map(item => editing === item.id
        ? <li key={item.id} className="fleet-form-row"><FleetForm farmId={farmId} categories={categories} item={item} onDone={closeForm} /></li>
        : <FleetRow key={item.id} farmId={farmId} item={item} category={category(item.equipmentCategoryId)} disabled={editing !== null} onEdit={() => setEditing(item.id)} />)}
    </ul>}
    {editing === 'new'
      ? <FleetForm farmId={farmId} categories={categories} onDone={closeForm} />
      : <button ref={addButton} type="button" className="button button-dark fleet-add" disabled={editing !== null} onClick={() => setEditing('new')}><Plus size={16} />Add machinery</button>}
  </section>
}

function FleetRow({ farmId, item, category, disabled, onEdit }: { farmId: string; item: FleetItem; category?: EquipmentCategory; disabled: boolean; onEdit: () => void }) {
  const deleteItem = useDeleteFleetItem(farmId)
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState('')
  const name = category?.name ?? 'Machinery type ' + item.equipmentCategoryId
  return <li className="fleet-row">
    <div className="fleet-type"><strong>{name}</strong>{item.customName && <span>{item.customName}</span>}<span>{machineHours(item)}</span></div>
    {confirming
      ? <div className="fleet-confirm" role="alertdialog" aria-label={'Remove ' + name + '?'}>
          <p>Remove {name} from this farm?</p>
          <div className="field-actions">
            <button type="button" className="button button-danger" disabled={deleteItem.isPending}
              onClick={async () => { try { await deleteItem.mutateAsync(item.id) } catch (reason) { setError(problemMessage(reason)) } }}>
              {deleteItem.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Remove machinery</button>
            <button type="button" className="button button-quiet" autoFocus onClick={() => setConfirming(false)}>Keep</button>
          </div>
          {error && <p className="field-error" role="alert">{error}</p>}
        </div>
      : <div className="fleet-actions">
          <button type="button" className="button button-quiet" disabled={disabled} onClick={onEdit}><PencilLine size={15} />Edit</button>
          <button type="button" className="button button-quiet button-icon" disabled={disabled} aria-label={'Remove ' + name} title="Remove machinery" onClick={() => setConfirming(true)}><Trash2 size={15} /></button>
        </div>}
  </li>
}

const hoursFormat = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 })

/** Machine operating hours (monthly_hours_per_unit), never operator shifts. */
function machineHours(item: Pick<FleetItem, 'annualHoursPerUnit' | 'unitsCount'>) {
  if (item.annualHoursPerUnit === null) return 'Machine hours: not recorded'
  const units = item.unitsCount === null ? 'unit count unknown' : `${item.unitsCount} ${item.unitsCount === 1 ? 'unit' : 'units'}`
  return `Machine hours: ${hoursFormat.format(item.annualHoursPerUnit)} h a year per unit · ${units}`
}

type FleetValues = { equipmentCategoryId: string; customName: string }
type FleetErrors = Partial<Record<keyof FleetValues, string>>

function FleetForm({ farmId, categories, item, onDone }: { farmId: string; categories: EquipmentCategory[]; item?: FleetItem; onDone: () => void }) {
  const saveItem = useSaveFleetItem(farmId)
  const [values, setValues] = useState<FleetValues>({ equipmentCategoryId: item ? String(item.equipmentCategoryId) : '', customName: item?.customName ?? '' })
  const [errors, setErrors] = useState<FleetErrors>({})
  const [formError, setFormError] = useState('')
  const set = (key: keyof FleetValues) => (event: { target: { value: string } }) => { setValues({ ...values, [key]: event.target.value }); setErrors({ ...errors, [key]: undefined }) }
  const selected = categories.find(category => String(category.id) === values.equipmentCategoryId)
  const id = item?.id ?? 'new'

  async function submit(event: FormEvent) {
    event.preventDefault()
    const next: FleetErrors = {}
    if (!selected) next.equipmentCategoryId = 'Choose a machinery type.'
    setErrors(next); setFormError('')
    if (Object.keys(next).length) return
    try {
      await saveItem.mutateAsync({ itemId: item?.id, input: { equipmentCategoryId: selected!.id, customName: values.customName.trim() || null } })
      onDone()
    } catch (reason) {
      const body = (reason as { body?: { errors?: Record<string, string[]> } }).body
      if (body?.errors) setErrors(Object.fromEntries(Object.entries(body.errors).map(([key, messages]) => [key, messages[0]])))
      else setFormError(problemMessage(reason))
    }
  }

  const field = (key: keyof FleetValues) => ({ 'aria-invalid': Boolean(errors[key]), 'aria-describedby': errors[key] ? key + '-error-' + id : undefined })
  const fieldError = (key: keyof FleetErrors) => errors[key] && <p id={key + '-error-' + id} className="field-error" role="alert">{errors[key]}</p>
  return <form className="fleet-form" onSubmit={submit} noValidate onKeyDown={event => { if (event.key === 'Escape' && !saveItem.isPending) onDone() }}
    aria-label={item ? 'Edit machinery' : 'Add machinery'}>
    <div className="fleet-form-grid">
      <div className="form-field fleet-form-type">
        <label htmlFor={'type-' + id}>Machinery type</label>
        <select id={'type-' + id} value={values.equipmentCategoryId} onChange={set('equipmentCategoryId')} disabled={saveItem.isPending} autoFocus {...field('equipmentCategoryId')}>
          <option value="" disabled>Choose a type</option>
          {categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
        </select>
        {selected?.description && <p className="input-hint">{selected.description}</p>}
        {fieldError('equipmentCategoryId')}
      </div>
      <div className="form-field">
        <label htmlFor={'model-' + id}>Make and model <span className="optional">optional</span></label>
        <input id={'model-' + id} value={values.customName} onChange={set('customName')} maxLength={100} disabled={saveItem.isPending} placeholder="e.g. John Deere 8R 410" {...field('customName')} />
        {fieldError('customName')}
      </div>
    </div>
    {formError && <div className="form-error" role="alert">{formError}</div>}
    <div className="field-actions">
      <button type="submit" className="button button-dark" disabled={saveItem.isPending}>{saveItem.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}Save machinery</button>
      <button type="button" className="button button-quiet" onClick={onDone} disabled={saveItem.isPending}>Cancel</button>
    </div>
  </form>
}
