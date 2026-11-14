import { useEffect, useRef, type KeyboardEvent } from 'react'
import { ChevronRight, CornerDownRight } from 'lucide-react'
import { FieldGlyph } from '@/features/map/FieldGlyph'
import { nestFields, type Field } from './api'
import { formatHectares } from './geo'
import { fieldCropLabel, type Crop, type Planting } from './plantings'

type FieldListProps = {
  fields: Field[]; plantings: Planting[]; crops: Crop[]; selectedId: string | null; hoveredId: string | null; savedId: string | null
  busy: boolean
  onSelect: (fieldId: string) => void; onHover: (fieldId: string | null) => void
}

/**
 * Fields of the farm as compact rows (outline, name, crop, area), with each field's subfields nested under it. Arrow keys
 * move the selection. The selected field's actions are in the field inspector over the map, so the list stays short.
 */
export function FieldList({ fields: all, plantings, crops, selectedId, hoveredId, savedId, busy, onSelect, onHover }: FieldListProps) {
  const fields = nestFields(all)
  const listRef = useRef<HTMLUListElement>(null)
  useEffect(() => {
    listRef.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [selectedId])

  function move(event: KeyboardEvent<HTMLUListElement>) {
    if (busy) return
    if (!['ArrowDown', 'ArrowUp'].includes(event.key) || ['INPUT', 'SELECT', 'TEXTAREA'].includes((event.target as HTMLElement).tagName)) return
    event.preventDefault()
    const index = fields.findIndex(field => field.id === selectedId)
    const next = fields[(index + (event.key === 'ArrowDown' ? 1 : fields.length - 1)) % fields.length] ?? fields[0]
    if (!next) return
    onSelect(next.id)
    listRef.current?.querySelector<HTMLButtonElement>(`[data-field="${next.id}"]`)?.focus()
  }

  return <ul ref={listRef} className="field-list" aria-label="Fields" onKeyDown={move}>
    {fields.map(field => {
      const selected = field.id === selectedId
      const crop = fieldCropLabel(field, all, plantings, crops)
      const children = all.filter(child => child.parentFieldId === field.id)
      const parent = field.parentFieldId ? all.find(candidate => candidate.id === field.parentFieldId) ?? null : null
      return <li key={field.id} className={`field-row ${parent ? 'is-subfield' : ''} ${children.length ? 'has-subfields' : ''} ${selected ? 'is-selected' : ''} ${hoveredId === field.id ? 'is-hovered' : ''} ${savedId === field.id ? 'is-saved' : ''}`}
        onMouseEnter={() => onHover(field.id)} onMouseLeave={() => onHover(null)}>
        <button type="button" className="field-row-main" data-field={field.id} aria-current={selected} disabled={busy} onClick={() => onSelect(field.id)}
          aria-controls={selected ? 'field-inspector' : undefined} aria-label={parent ? `${field.name}, subfield of ${parent.name}` : undefined}>
          {parent && <CornerDownRight size={13} className="field-row-branch" aria-hidden="true" />}
          <FieldGlyph boundary={field.boundary} />
          <span className="field-row-text">
            <span className="field-row-name">{field.name}{children.length > 0 && <span className="field-row-count">{children.length} {children.length === 1 ? 'subfield' : 'subfields'}</span>}</span>
            <span className={`field-row-crop ${crop.growing ? '' : 'is-empty'}`} title={crop.text}>{crop.text}</span>
          </span>
          <span className="field-row-area mono">{formatHectares(field.areaHectares)}</span>
          <ChevronRight size={14} className="field-row-chevron" aria-hidden="true" />
        </button>
      </li>
    })}
  </ul>
}
