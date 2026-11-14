import { useState } from 'react'
import { Plus } from 'lucide-react'
import type { Field } from './api'
import { FieldTimeline } from './FieldTimeline'
import { PlantingModal } from './PlantingModal'
import { EARLIEST_SEASON, LATEST_SEASON, dayAfter, periodText, thisYear, type Slot } from './periods'
import { cropName, type Crop, type Planting } from './plantings'
import { cropColour, openEnd, timelineRange } from './timeline'
import type { GanttRow } from '@/features/strategies/details'
import { StrategyGantt } from '@/features/strategies/StrategyGantt'

type Editing = { kind: 'new'; slot: Slot } | { kind: 'edit'; planting: Planting; focusHarvest?: boolean } | null

const todayIso = () => new Date().toISOString().slice(0, 10)

/**
 * A field's crop plan on a time axis. A bar opens its crop in the modal; "Add crop" suggests the day after the last
 * harvest. Crops never overlap, so a crop still growing without a harvest date must be closed before the next one.
 * `layout="rows"` draws one row per crop with sowing, growth and harvest, like a strategy's details; a row opens its crop.
 */
export function CropTimeline({ field, plantings, crops, disabled, title = 'Crop plan', layout = 'strip' }: {
  field: Field; plantings: Planting[]; crops: Crop[]; disabled: boolean; title?: string; layout?: 'strip' | 'rows'
}) {
  const [editing, setEditing] = useState<Editing>(null)
  const periods = [...plantings].sort((a, b) => a.plantingDate.localeCompare(b.plantingDate) || a.id.localeCompare(b.id))
  const last = periods.at(-1)
  const today = todayIso()
  const range = timelineRange(periods, today)
  const open = last && !last.harvestingDate ? last : null

  function addCrop() {
    if (open) { setEditing({ kind: 'edit', planting: open, focusHarvest: true }); return }
    const start = last?.harvestingDate ? dayAfter(last.harvestingDate) : ''
    const season = Math.min(Math.max(start ? Number(start.slice(0, 4)) : thisYear, EARLIEST_SEASON), LATEST_SEASON)
    setEditing({ kind: 'new', slot: { season: { value: season, min: EARLIEST_SEASON, max: Math.max(LATEST_SEASON, season) }, plantingDate: start, maxHarvest: null } })
  }

  return <section className="crop-history" aria-label={`Crops on ${field.name}`}>
    <div className="crop-history-head">
      <h3>{title}</h3>
      <button type="button" className="button button-quiet" disabled={disabled || crops.length === 0} onClick={addCrop}
        title={open ? `Set a harvest date for ${cropName(crops, open.cropId)} first` : undefined}>
        <Plus size={14} /><span>{open ? `Close ${cropName(crops, open.cropId)} to add a crop` : 'Add crop'}</span>
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
      {...(editing.kind === 'new' ? { slot: editing.slot } : { planting: editing.planting, focusHarvest: editing.focusHarvest })} />}
  </section>
}
