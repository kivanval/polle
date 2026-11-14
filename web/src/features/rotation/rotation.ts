import type { Field } from '@/features/farms/api'
import type { TimelineBar } from '@/features/farms/FieldTimeline'
import { periodText } from '@/features/farms/periods'
import { cropName, currentPlanting, nextPlanned, type Crop, type Planting } from '@/features/farms/plantings'
import { cropColour } from '@/features/farms/timeline'

/** Where a field's rotation stands: the crop growing now, the next one sown, and how many are planned ahead. */
export type FieldPlan = { growing: Planting | null; next: Planting | null; ahead: number }

/** A field's plan from its own plantings (statuses come from the API, evaluated on today's date). */
export function fieldPlan(plantings: Planting[]): FieldPlan {
  return {
    growing: currentPlanting(plantings),
    next: nextPlanned(plantings),
    ahead: plantings.filter(planting => planting.status === 'planned').length,
  }
}

/** The field's crops as timeline bars in sowing order, coloured per crop; a crop without a harvest date runs on. */
export function planBars(plantings: Planting[], crops: Crop[]): TimelineBar[] {
  return [...plantings]
    .sort((a, b) => a.plantingDate.localeCompare(b.plantingDate) || a.id.localeCompare(b.id))
    .map(planting => ({
      key: planting.id, label: cropName(crops, planting.cropId), detail: periodText(planting),
      from: planting.plantingDate, to: planting.harvestingDate, colour: cropColour(planting.cropId),
    }))
}

export type RotationSummary = { fields: number; withPlan: number; withoutPlan: number; noSoil: number; upcoming: number }

/**
 * Counts over every field and subfield shown: a field "has a plan" when a crop grows on it now or is planned ahead;
 * `upcoming` is the number of planned crops; `noSoil` fields cannot be sent to the solver.
 */
export function rotationSummary(fields: Field[], plantings: Planting[]): RotationSummary {
  const ids = new Set(fields.map(field => field.id))
  const own = plantings.filter(planting => ids.has(planting.fieldId))
  const withPlan = fields.filter(field => {
    const plan = fieldPlan(own.filter(planting => planting.fieldId === field.id))
    return plan.growing !== null || plan.ahead > 0
  }).length
  return {
    fields: fields.length, withPlan, withoutPlan: fields.length - withPlan,
    noSoil: fields.filter(field => !field.soilAvailable).length,
    upcoming: own.filter(planting => planting.status === 'planned').length,
  }
}
