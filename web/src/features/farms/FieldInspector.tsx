import { useState, type FormEvent, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, Droplets, LayoutList, PencilLine, Spline, SquareSplitHorizontal, Trash2, X } from 'lucide-react'
import { FieldGlyph } from '@/features/map/FieldGlyph'
import type { Field } from './api'
import { conditionsOf, conditionsText, type FieldConditions } from './conditions'
import { FieldConditionsFields } from './FieldConditionsFields'
import { CropTimeline } from './CropTimeline'
import { formatHectares, vertexCount } from './geo'
import { fieldCropLabel, type Crop, type Planting } from './plantings'
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog'
import { TractorSpinner } from '@/shared/ui/Tractor'
import { useSavedStrategies } from '@/features/strategies/api'
import { PlanStrategiesView } from '@/features/strategies/PlanStrategiesView'
import { usePlanParam } from '@/features/strategies/planParam'

export type FieldInspectorProps = {
  field: Field; fields: Field[]; plantings: Planting[]; crops: Crop[]; busy: boolean; error: string
  onClose: () => void; onSelect: (fieldId: string) => void
  onRename: (field: Field, name: string) => Promise<boolean>; onEditBoundary: (field: Field) => void
  /** Deletes the field with its subfields, crop history and saved strategies; resolves to an error message, empty once deleted. */
  onDelete: (field: Field) => Promise<string>
  onAddSubfield: (field: Field) => void
  /** Saves the field's drought risk, soil texture and drainage; true when saved. */
  onSaveConditions: (field: Field, conditions: FieldConditions) => Promise<boolean>
}

/**
 * The selected field, docked over the map: what it is, its actions in one toolbar, its crop plan and the way into plans and
 * strategies. Keyed by field where it is used, so rename and confirm state reset when the selection moves.
 */
export function FieldInspector({ field, fields, plantings: all, crops, busy, error, onClose, onSelect, onRename, onEditBoundary, onDelete, onAddSubfield, onSaveConditions }: FieldInspectorProps) {
  const [renaming, setRenaming] = useState(false)
  const [describing, setDescribing] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [planField, setPlanField] = usePlanParam()
  const planning = planField === field.id
  const parent = field.parentFieldId ? fields.find(candidate => candidate.id === field.parentFieldId) ?? null : null
  const subfields = fields.filter(child => child.parentFieldId === field.id)
  const plantings = all.filter(planting => planting.fieldId === field.id)
  const subfieldCrops = all.filter(planting => subfields.some(child => child.id === planting.fieldId)).length
  const crop = fieldCropLabel(field, fields, all, crops)
  const known = conditionsText(field)
  const editing = renaming || describing

  /** Escape on the inspector itself deselects the field; forms, dialogs and drawers inside handle their own. */
  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key !== 'Escape' || busy || editing || confirmDelete || planning) return
    const target = event.target as HTMLElement
    if (!event.currentTarget.contains(target) || target.closest('dialog, [role="dialog"], form, input, select, textarea')) return
    onClose()
  }

  const tool = (label: string, icon: ReactNode, onClick: () => void, extra: { pressed?: boolean; title?: string } = {}) =>
    <button type="button" className="inspector-tool" disabled={busy} aria-pressed={extra.pressed} title={extra.title} onClick={onClick}>
      {icon}<span>{label}</span></button>

  return <section id="field-inspector" className={`field-inspector ${collapsed ? 'is-collapsed' : ''}`} aria-labelledby="field-inspector-title" onKeyDown={onKeyDown}>
    <header className="inspector-head">
      <FieldGlyph boundary={field.boundary} />
      <div className="inspector-title">
        <span className="inspector-kicker">{parent
          ? <>Subfield of <button type="button" className="text-button" disabled={busy} onClick={() => onSelect(parent.id)}>{parent.name}</button></>
          : 'Selected field'}</span>
        <h2 id="field-inspector-title">{field.name}</h2>
        <p className={`inspector-crop ${crop.growing ? '' : 'is-empty'}`} title={crop.text}>{crop.text}</p>
      </div>
      <div className="inspector-head-actions">
        <button type="button" className="inspector-icon" aria-expanded={!collapsed} aria-controls="field-inspector-content"
          aria-label={collapsed ? 'Show field details' : 'Hide field details'} title={collapsed ? 'Show details' : 'Hide details'}
          onClick={() => setCollapsed(!collapsed)}><ChevronDown size={16} /></button>
        <button type="button" className="inspector-icon" aria-label="Close field" title="Close (Esc)" disabled={busy} onClick={onClose}><X size={16} /></button>
      </div>
    </header>

    <div id="field-inspector-content" className="inspector-content" hidden={collapsed}>
      <dl className="inspector-stats">
        <div><dt>Area</dt><dd className="mono">{formatHectares(field.areaHectares)}</dd></div>
        {parent
          ? <div><dt>Of {parent.name}</dt><dd className="mono">{Math.round((field.areaHectares / parent.areaHectares) * 100)}%</dd></div>
          : <div><dt>Subfields</dt><dd className="mono">{subfields.length}</dd></div>}
        <div><dt>Corners</dt><dd className="mono">{vertexCount(field.boundary)}</dd></div>
      </dl>

      <div className="inspector-toolbar" role="toolbar" aria-label={`Actions for ${field.name}`}>
        {tool('Rename', <PencilLine size={16} />, () => { setDescribing(false); setRenaming(!renaming) }, { pressed: renaming })}
        {tool('Boundary', <Spline size={16} />, () => onEditBoundary(field), { title: 'Drag the corners on the map to reshape the field' })}
        {tool('Soil & climate', <Droplets size={16} />, () => { setRenaming(false); setDescribing(!describing) },
          { pressed: describing, title: 'Drought risk, soil texture and drainage used by the planner' })}
        {!parent && tool('Subfield', <SquareSplitHorizontal size={16} />, () => onAddSubfield(field), { title: 'Draw a smaller area inside this field to manage separately' })}
        <button type="button" className="inspector-tool is-danger" disabled={busy} aria-label={`Delete ${field.name}`} title="Delete field"
          onClick={() => setConfirmDelete(true)}><Trash2 size={16} /><span aria-hidden="true">Delete</span></button>
      </div>

      <div className="inspector-body">
        {renaming
          ? <RenameForm field={field} busy={busy} onCancel={() => setRenaming(false)}
              onSave={async name => { if (await onRename(field, name)) setRenaming(false) }} />
          : describing
          ? <ConditionsForm field={field} busy={busy} onCancel={() => setDescribing(false)}
              onSave={async conditions => { if (await onSaveConditions(field, conditions)) setDescribing(false) }} />
          : <p className={`inspector-conditions ${known ? '' : 'is-empty'}`}><Droplets size={13} aria-hidden="true" />
              {known || 'Soil and drought risk not set. Suggested plans use an estimate.'}</p>}
        {error && <p className="field-error" role="alert">{error}</p>}
        {!editing && <CropTimeline field={field} plantings={plantings} crops={crops} disabled={busy} />}
      </div>

      {!editing && <footer className="inspector-foot">
        <button type="button" className="button button-dark inspector-plan" disabled={busy || crops.length === 0} onClick={() => setPlanField(field.id)}>
          <LayoutList size={16} />Plan &amp; suggestions</button>
      </footer>}
    </div>

    {confirmDelete && <DeleteFieldDialog field={field} subfields={subfields} crops={plantings.length + subfieldCrops}
      onDelete={onDelete} onClose={() => setConfirmDelete(false)} />}
    {/* Full-screen view: rendered at the page root, so the docked panel's blur and motion can't clip or trap it. */}
    {planning && createPortal(<PlanStrategiesView field={field} plantings={plantings} crops={crops} onClose={() => setPlanField(null)} />, document.body)}
  </section>
}

/**
 * Asks before deleting a field and names what goes with it: its subfields, crop history and saved strategies, which
 * the API deletes in one go. Expenses booked to the field stay in the ledger without it.
 */
function DeleteFieldDialog({ field, subfields, crops, onDelete, onClose }: {
  field: Field; subfields: Field[]; crops: number; onDelete: (field: Field) => Promise<string>; onClose: () => void
}) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const strategies = useSavedStrategies(field.id).data?.length ?? 0
  const count = (value: number, one: string, many: string) => `${value} ${value === 1 ? one : many}`
  const losses = [
    subfields.length > 0 && count(subfields.length, 'subfield', 'subfields'),
    crops > 0 && `${count(crops, 'crop', 'crops')} in the crop plan and history`,
    strategies > 0 && count(strategies, 'saved plan', 'saved plans'),
  ].filter((item): item is string => Boolean(item))
  async function confirm() {
    setPending(true); setError('')
    const message = await onDelete(field)
    setPending(false)
    if (message) setError(message); else onClose()
  }
  return <ConfirmDialog title={`Delete ${field.name}?`} confirmLabel="Delete field" cancelLabel="Keep field" icon={<Trash2 size={15} />}
    busy={pending} error={error} onConfirm={() => void confirm()} onCancel={onClose}>
    <p>Its boundary is removed from the farm{losses.length ? ', and these are deleted with it:' : '.'}</p>
    {losses.length > 0 && <ul>{losses.map(item => <li key={item}>{item}</li>)}</ul>}
    <p>Every crop plan and saved plan for {subfields.length ? 'the field and its subfields' : 'this field'} is deleted too.
      Expenses booked to it stay in the ledger without a field. This can’t be undone.</p>
  </ConfirmDialog>
}

function ConditionsForm({ field, busy, onSave, onCancel }: { field: Field; busy: boolean; onSave: (conditions: FieldConditions) => void; onCancel: () => void }) {
  const [conditions, setConditions] = useState(() => conditionsOf(field))
  function submit(event: FormEvent) {
    event.preventDefault()
    if (!busy) onSave(conditions)
  }
  return <form className="field-conditions-form" onSubmit={submit} aria-label={`Soil and climate of ${field.name}`}
    onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel() }}>
    <p className="field-conditions-note">Suggested plans use these instead of the estimated soil. Leave “Not sure” if you don’t know.</p>
    <FieldConditionsFields value={conditions} disabled={busy} onChange={setConditions} />
    <div className="field-actions">
      <button type="submit" className="button button-dark" disabled={busy}>{busy && <TractorSpinner size={15} />}Save</button>
      <button type="button" className="button button-quiet" disabled={busy} onClick={onCancel}>Cancel</button>
    </div>
  </form>
}

function RenameForm({ field, busy, onSave, onCancel }: { field: Field; busy: boolean; onSave: (name: string) => void; onCancel: () => void }) {
  const [name, setName] = useState(field.name)
  function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    if (name.trim() && name.trim() !== field.name) onSave(name.trim())
    else onCancel()
  }
  return <form className="field-rename" onSubmit={submit}>
    <label htmlFor="rename-field" className="visually-hidden">Field name</label>
    <input id="rename-field" value={name} maxLength={150} autoFocus disabled={busy} onFocus={event => event.target.select()}
      onChange={event => setName(event.target.value)} onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel() }} />
    <div className="field-actions">
      <button type="submit" className="button button-dark" disabled={busy || !name.trim()}>{busy && <TractorSpinner size={15} />}Save name</button>
      <button type="button" className="button button-quiet" disabled={busy} onClick={onCancel}>Cancel</button>
    </div>
  </form>
}
