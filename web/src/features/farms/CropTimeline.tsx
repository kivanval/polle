import { useState } from 'react'
import { Plus } from 'lucide-react'
import type { Field } from './api'
import { FieldTimeline } from './FieldTimeline'
import { PlantingModal } from './PlantingModal'
import { newCropSlot, periodText, type Slot } from './periods'
import { cropName, type Crop, type Planting } from './plantings'
import { cropColour, openEnd, timelineRange } from './timeline'
import type { GanttRow } from '@/features/strategies/details'
import { StrategyGantt } from '@/features/strategies/StrategyGantt'

type Editing = { kind: 'new'; slot: Slot } | { kind: 'edit'; planting: Planting } | null

const todayIso = () => new Date().toISOString().slice(0, 10)

/**
 * A field's crop plan on a time axis. A bar opens its crop in the modal; "Add a crop" is always open and suggests the day
 * after the last harvest. Crops never overlap: a crop still growing without a harvest date holds the field from its
 * sowing on, so the form offers the free days before it (e.g. a 2026 crop ahead of an onion sown in 2027).
 * `layout="rows"` draws one row per crop with sowing, growth and harvest, like a strategy's details; a row opens its crop.
 */
export function CropTimeline({ field, plantings, crops, disabled, title = 'Crop plan', layout = 'strip' }: {
  field: Field; plantings: Planting[]; crops: Crop[]; disabled: boolean; title?: string; layout?: 'strip' | 'rows'
}) {
  const [editing, setEditing] = useState<Editing>(null)
  const periods = [...plantings].sort((a, b) => a.plantingDate.localeCompare(b.plantingDate) || a.id.localeCompare(b.id))
  const today = todayIso()
  const range = timelineRange(periods, today)

  return <section className="crop-history" aria-label={`Crops on ${field.name}`}>
    <div className="crop-history-head">
      <h3>{title}</h3>
      <button type="button" className="button button-quiet" disabled={disabled || crops.length === 0}
        onClick={() => setEditing({ kind: 'new', slot: newCropSlot(periods) })}>
        <Plus size={14} /><span>Add a crop</span>
      </button>
    </div>
    {crops.length === 0
      ? <p className="crop-empty">Loading the crop catalog…</p>
      : periods.length === 0
        ? <p className="crop-empty">No crops yet. Add what grows on this field to see its plan over the years.</p>
        : layout === 'rows'
          ? <StrategyGantt label={`Crop plan of ${field.name}`} today={today} firstYear={Number(range.start.slice(0, 4))} lastYear={Number(range.end.slice(0, 4))}
              groups={[{ key: 'plan', label: title, rows: periods.map((planting): GanttRow => ({
                key: planting.id, cropId: planting.cropId, cropName: cropName(crops, planting.cropId), plantingMonth: planting.plantingDate.slice(0, 7),
                clearanceMonth: (planting.harvestingDate ?? openEnd(planting, today)).slice(0, 7),
                harvestMonths: planting.harvestingDate ? [planting.harvestingDate.slice(0, 7)] : [], plantingDate: planting.plantingDate,
                clearanceDate: planting.harvestingDate, risks: [], warnings: [],
                onSelect: disabled ? undefined : () => setEditing({ kind: 'edit', planting }) })) }]} />
          : <FieldTimeline label={`Crop plan of ${field.name}`} range={range} today={today} lanes={[{ key: 'plan', bars: periods.map(planting => ({
            key: planting.id, label: cropName(crops, planting.cropId), detail: periodText(planting), from: planting.plantingDate, to: planting.harvestingDate ?? openEnd(planting, today), open: !planting.harvestingDate,
            colour: cropColour(planting.cropId), onClick: disabled ? undefined : () => setEditing({ kind: 'edit', planting }) })) }]} />}
    {editing && <PlantingModal field={field} crops={crops} periods={periods} onClose={() => setEditing(null)}
      {...(editing.kind === 'new' ? { slot: editing.slot } : { planting: editing.planting })} />}
  </section>
}
