import { useEffect, useEffectEvent, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Check, Plus, Trash2, X } from 'lucide-react'
import { ApiError } from '@/shared/api/client'
import { keyOf, useDeleteCatalogRow, useSaveCatalogRow, type CatalogColumn, type CatalogOption, type CatalogRow, type CatalogTable, type RowValues } from './api'
import { columnLabel } from './format'
import { TractorSpinner } from '@/shared/ui/Tractor'

type FormValue = string | boolean | Record<string, string>
type Form = Record<string, FormValue>

function initial(table: CatalogTable, row?: CatalogRow): Form {
  return Object.fromEntries(table.columns.map(column => {
    const value = row?.values[column.name]
    if (column.kind === 'translations') return [column.name, row ? { ...(value as Record<string, string> ?? {}) } : { en: '', uk: '' }]
    if (column.kind === 'boolean') return [column.name, Boolean(value)]
    if (column.kind === 'json') return [column.name, value == null ? '' : JSON.stringify(value, null, 2)]
    return [column.name, value == null ? '' : String(value)]
  }))
}

/** Form state to API values: blank optional cells become null; on a new row, blank cells with a database default are left to it. */
function toValues(table: CatalogTable, form: Form, isNew: boolean, errors: Record<string, string>): RowValues {
  const values: RowValues = {}
  for (const column of table.columns) {
    if (column.generated || (!isNew && column.key) || column.kind === 'unsupported') continue
    const value = form[column.name]
    if (column.kind === 'translations') {
      values[column.name] = Object.fromEntries(Object.entries(value as Record<string, string>).map(([lang, text]) => [lang, text.trim()]).filter(([, text]) => text))
    } else if (column.kind === 'boolean') {
      values[column.name] = value
    } else {
      const text = (value as string).trim()
      if (!text) {
        if (isNew && column.hasDefault) continue
        if (!column.nullable) { errors[column.name] = 'Required.'; continue }
        values[column.name] = null
      } else if (column.kind === 'json') {
        try { values[column.name] = JSON.parse(text) } catch { errors[column.name] = 'This is not valid JSON.' }
      } else values[column.name] = text
    }
  }
  return values
}

type Props = {
  table: CatalogTable; row: CatalogRow | null; links: Record<string, CatalogOption[]>; titles: Record<string, string>
  onClose: () => void; onSaved: (message: string) => void
}

/** Slide-over form for one row; a new row when `row` is null. Deleting first shows what cascading links would also remove. */
export function RowEditor({ table, row, links, titles, onClose, onSaved }: Props) {
  const isNew = row === null
  const save = useSaveCatalogRow(table.name)
  const remove = useDeleteCatalogRow(table.name)
  const [form, setForm] = useState<Form>(() => initial(table, row ?? undefined))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState('')
  const [cascade, setCascade] = useState<Record<string, number> | null>(null)
  const panel = useRef<HTMLElement>(null)
  const busy = save.isPending || remove.isPending
  const name = row?.label || table.title

  // Saving disables the buttons, which drops focus to the page; listen for Escape on the document so it still closes.
  const escape = useEffectEvent((event: globalThis.KeyboardEvent) => { if (event.key === 'Escape' && !busy) onClose() })
  useEffect(() => {
    const listener = (event: globalThis.KeyboardEvent) => escape(event)
    document.addEventListener('keydown', listener)
    return () => document.removeEventListener('keydown', listener)
  }, [])
  const focusProblem = () => requestAnimationFrame(() =>
    panel.current?.querySelector<HTMLElement>('[aria-invalid=true], .catalog-form-error, .catalog-confirm button')?.focus())

  function set(column: string, value: FormValue) { setForm(current => ({ ...current, [column]: value })); setErrors(current => ({ ...current, [column]: '' })); setFormError('') }

  function fail(reason: unknown) {
    const body = reason instanceof ApiError ? reason.body as { errors?: Record<string, string[]>; detail?: string } : null
    const fieldErrors = Object.fromEntries(Object.entries(body?.errors ?? {}).map(([key, messages]) => [key, messages[0]]))
    setErrors(fieldErrors)
    const general = ['row', 'values', 'key'].map(key => fieldErrors[key]).find(Boolean)
    setFormError(general ?? (body?.errors ? '' : reason instanceof ApiError ? body?.detail ?? reason.message : 'We couldn’t reach Polle. Check your connection and try again.'))
    focusProblem()
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    const clientErrors: Record<string, string> = {}
    const values = toValues(table, form, isNew, clientErrors)
    setErrors(clientErrors); setFormError('')
    if (Object.keys(clientErrors).length) { focusProblem(); return }
    try {
      const saved = await save.mutateAsync({ key: row ? keyOf(table, row.values) : undefined, values })
      onSaved(isNew ? `Added ${saved.label || 'the row'}.` : `Saved ${saved.label || 'the row'}.`)
    } catch (reason) { fail(reason) }
  }

  async function startDelete() {
    if (!row) return
    setFormError('')
    try { setCascade((await remove.mutateAsync({ key: keyOf(table, row.values), preview: true })).cascade) } catch (reason) { fail(reason) }
  }
  async function confirmDelete() {
    if (!row) return
    try { await remove.mutateAsync({ key: keyOf(table, row.values), preview: false }); onSaved(`Deleted ${name}.`) } catch (reason) { setCascade(null); fail(reason) }
  }

  // Keep keyboard focus inside the panel while it is open; Escape closes it.
  function onKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Tab' || !panel.current) return
    const focusable = [...panel.current.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled)')]
    const first = focusable[0], last = focusable.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }

  const editable = table.columns.filter(column => !(isNew && column.generated))
  return <>
    <button type="button" className="catalog-backdrop" aria-label="Close the editor" tabIndex={-1} onClick={() => { if (!busy) onClose() }} />
    <aside ref={panel} className="catalog-drawer" role="dialog" aria-modal="true" aria-labelledby="catalog-drawer-title" onKeyDown={onKeyDown}>
      <header className="catalog-drawer-head">
        <div><span className="catalog-drawer-kicker">{table.title}</span><h2 id="catalog-drawer-title">{isNew ? 'New row' : name}</h2></div>
        <button type="button" className="button button-quiet button-icon" aria-label="Close" onClick={onClose} disabled={busy}><X size={16} /></button>
      </header>
      <form className="catalog-form" onSubmit={submit} noValidate id="catalog-form">
        {editable.map((column, index) => <Field key={column.name} column={column} value={form[column.name]} error={errors[column.name]}
          readOnly={column.generated || (!isNew && column.key) || column.kind === 'unsupported'} autoFocus={index === editable.findIndex(c => !(c.generated || (!isNew && c.key)))}
          options={column.link ? links[column.link.table] : undefined} linkTitle={column.link ? titles[column.link.table] : undefined}
          disabled={busy} onChange={value => set(column.name, value)} />)}
        {!isNew && table.key.length > 1 && <p className="catalog-note">The key ({table.key.join(', ')}) can’t be changed. Delete this row and add a new one instead.</p>}
      </form>
      {formError && <p className="form-error catalog-form-error" role="alert" tabIndex={-1}>{formError}</p>}
      {cascade
        ? <div className="catalog-confirm" role="alertdialog" aria-labelledby="catalog-confirm-title">
            <p id="catalog-confirm-title"><strong>Delete {name}?</strong></p>
            {Object.keys(cascade).length > 0
              ? <><p>Linked rows will be deleted with it:</p><ul>{Object.entries(cascade).map(([other, count]) => <li key={other}><strong>{count}</strong> {titles[other] ?? other}</li>)}</ul></>
              : <p>No other rows depend on it.</p>}
            <div className="field-actions">
              <button type="button" className="button button-danger" onClick={() => void confirmDelete()} disabled={busy}>
                {remove.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Delete row</button>
              <button type="button" className="button button-quiet" autoFocus onClick={() => setCascade(null)} disabled={busy}>Keep row</button>
            </div>
          </div>
        : <footer className="catalog-drawer-foot">
            <button type="submit" form="catalog-form" className="button button-dark" disabled={busy}>
              {save.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}{isNew ? 'Add row' : 'Save changes'}</button>
            <button type="button" className="button button-quiet" onClick={onClose} disabled={busy}>Cancel</button>
            {!isNew && <button type="button" className="button button-quiet catalog-delete" onClick={() => void startDelete()} disabled={busy}>
              {remove.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Delete…</button>}
          </footer>}
    </aside>
  </>
}

type FieldProps = {
  column: CatalogColumn; value: FormValue; error?: string; readOnly: boolean; autoFocus: boolean; disabled: boolean
  options?: CatalogOption[]; linkTitle?: string; onChange: (value: FormValue) => void
}

function Field({ column, value, error, readOnly, autoFocus, disabled, options, linkTitle, onChange }: FieldProps) {
  const id = `field-${column.name}`
  const { label, unit } = columnLabel(column.name)
  const described = error ? `${id}-error` : undefined
  const optional = column.nullable || column.hasDefault
  const common = { id, disabled: disabled || readOnly, 'aria-invalid': Boolean(error), 'aria-describedby': described, autoFocus }
  let input
  if (column.kind === 'translations') {
    const names = value as Record<string, string>
    input = <TranslationsInput id={id} names={names} disabled={disabled} autoFocus={autoFocus} onChange={onChange} />
  } else if (column.kind === 'boolean') {
    input = <label className="catalog-switch"><input type="checkbox" {...common} checked={value as boolean} onChange={event => onChange(event.target.checked)} />
      <span>{value ? 'Yes' : 'No'}</span></label>
  } else if (column.link && options && !readOnly) {
    input = <select {...common} value={value as string} onChange={event => onChange(event.target.value)}>
      <option value="">{optional ? 'None' : `Choose ${linkTitle?.toLowerCase() ?? 'a row'}`}</option>
      {options.map(option => <option key={String(option.key)} value={String(option.key)}>{option.label || `#${option.key}`}</option>)}
    </select>
  } else if (column.kind === 'enum') {
    input = <select {...common} value={value as string} onChange={event => onChange(event.target.value)}>
      <option value="">{optional ? 'None' : 'Choose…'}</option>
      {column.options?.map(option => <option key={option} value={option}>{option}</option>)}
    </select>
  } else if (column.kind === 'json' || (column.kind === 'text' && column.maxLength === null)) {
    input = <textarea {...common} rows={column.kind === 'json' ? 5 : 3} value={value as string} onChange={event => onChange(event.target.value)} spellCheck={column.kind !== 'json'} />
  } else {
    const numeric = column.kind === 'integer' || column.kind === 'decimal'
    input = <input {...common} value={value as string} onChange={event => onChange(event.target.value)} maxLength={column.maxLength ?? undefined}
      inputMode={column.kind === 'integer' ? 'numeric' : column.kind === 'decimal' ? 'decimal' : undefined} type={column.kind === 'date' ? 'date' : 'text'}
      className={numeric ? 'is-numeric' : undefined} placeholder={readOnly && column.generated ? 'Set automatically' : undefined} />
  }
  return <div className={`catalog-field ${column.kind === 'translations' ? 'is-wide' : ''}`}>
    <label htmlFor={id}>{label}{unit && <span className="catalog-unit">{unit}</span>}
      {readOnly ? <span className="optional">read-only</span> : optional && <span className="optional">optional</span>}</label>
    {input}
    {error && <p id={described} className="field-error" role="alert">{error}</p>}
  </div>
}

function TranslationsInput({ id, names, disabled, autoFocus, onChange }: { id: string; names: Record<string, string>; disabled: boolean; autoFocus: boolean; onChange: (value: FormValue) => void }) {
  const [code, setCode] = useState('')
  const entries = Object.entries(names).sort(([a], [b]) => (a === 'en' ? -1 : b === 'en' ? 1 : a.localeCompare(b)))
  const add = () => { const lang = code.trim().toLowerCase(); if (/^[a-z]{2}$/.test(lang) && !(lang in names)) { onChange({ ...names, [lang]: '' }); setCode('') } }
  return <div className="translations">
    {entries.map(([lang, text], index) => <div className="translation" key={lang}>
      <span className="translation-lang mono" aria-hidden="true">{lang}</span>
      <input id={index === 0 ? id : `${id}-${lang}`} aria-label={`Name in ${lang}`} value={text} disabled={disabled} autoFocus={autoFocus && index === 0} maxLength={200}
        onChange={event => onChange({ ...names, [lang]: event.target.value })} />
      {lang === 'en' ? <span aria-hidden="true" /> : <button type="button" className="button button-quiet button-icon" aria-label={`Remove ${lang}`} disabled={disabled}
        onClick={() => onChange(Object.fromEntries(Object.entries(names).filter(([other]) => other !== lang)))}><X size={14} /></button>}
    </div>)}
    <div className="translation-add">
      <input aria-label="Language code" placeholder="es" value={code} maxLength={2} disabled={disabled} onChange={event => setCode(event.target.value)}
        onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); add() } }} />
      <button type="button" className="button button-quiet" disabled={disabled || !/^[a-zA-Z]{2}$/.test(code.trim())} onClick={add}><Plus size={14} />Add language</button>
    </div>
  </div>
}
