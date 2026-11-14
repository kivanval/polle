import { useId, useState, type KeyboardEvent } from 'react'
import { cropMeta, searchCrops } from './cropSearch'
import type { Crop } from './plantings'

/**
 * Type-to-filter crop chooser over the solver catalog (about a thousand species, too many for a select).
 * ARIA combobox: arrows move the highlight, Enter picks, Escape restores the chosen crop's name.
 */
export function CropPicker({ id, crops, value, disabled, autoFocus, invalid, describedBy, onChange }: {
  id: string; crops: Crop[]; value: Crop | null; disabled: boolean; autoFocus?: boolean; invalid?: boolean; describedBy?: string
  onChange: (crop: Crop) => void
}) {
  const listId = useId()
  const [query, setQuery] = useState(value?.name ?? '')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const results = open ? searchCrops(crops, query === value?.name ? '' : query) : []

  function pick(crop: Crop) {
    onChange(crop)
    setQuery(crop.name)
    setOpen(false)
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) { setOpen(true); setActive(0); return }
      setActive(current => (current + (event.key === 'ArrowDown' ? 1 : results.length - 1)) % Math.max(results.length, 1))
    } else if (event.key === 'Enter' && open && results[active]) {
      event.preventDefault()
      pick(results[active])
    } else if (event.key === 'Escape' && open) {
      event.stopPropagation()
      setOpen(false)
      setQuery(value?.name ?? '')
    }
  }

  return <div className="crop-picker">
    <input id={id} role="combobox" aria-expanded={open} aria-controls={listId} aria-autocomplete="list" autoComplete="off"
      aria-activedescendant={open && results[active] ? `${listId}-${results[active].id}` : undefined}
      aria-invalid={invalid || undefined} aria-describedby={describedBy}
      value={query} disabled={disabled} autoFocus={autoFocus} placeholder="Type a crop name, e.g. wheat"
      onFocus={event => { event.currentTarget.select(); setOpen(true); setActive(0) }}
      onBlur={() => { setOpen(false); setQuery(value?.name ?? '') }}
      onChange={event => { setQuery(event.target.value); setOpen(true); setActive(0) }}
      onKeyDown={onKeyDown} />
    {open && <ul id={listId} role="listbox" className="crop-picker-list" aria-label="Crops">
      {results.length === 0
        ? <li className="crop-picker-empty" role="presentation">No crop matches “{query.trim()}”.</li>
        : results.map((crop, index) => <li key={crop.id} id={`${listId}-${crop.id}`} role="option" aria-selected={index === active}
            className={index === active ? 'is-active' : ''} onMouseDown={event => { event.preventDefault(); pick(crop) }}
            onMouseEnter={() => setActive(index)}>
            <span>{crop.name}</span>
            {cropMeta(crop) && <span className="crop-picker-meta">{cropMeta(crop)}</span>}
          </li>)}
    </ul>}
  </div>
}
