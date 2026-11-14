/**
 * What the farmer knows about a field's soil and climate, null when unknown. The classes are the solver's names; the
 * starting values come from a soil test: plant-available N, P and K in kg/ha (the form asks for mg/kg, see
 * KG_HA_PER_MG_KG) and the root zone's water deficit in mm below field capacity (0 = fully wet).
 */
export type FieldConditions = {
  droughtLevel: string | null; soilDrainage: string | null; soilTexture: string | null
  initialNKg: number | null; initialPKg: number | null; initialKKg: number | null; initialDepletionMm: number | null
}

export const NO_CONDITIONS: FieldConditions = {
  droughtLevel: null, soilDrainage: null, soilTexture: null, initialNKg: null, initialPKg: null, initialKKg: null, initialDepletionMm: null,
}

/**
 * Soil labs report N, P and K as a concentration, mg per kg of soil; the API and solver take kg/ha. A sample of the
 * 0–30 cm plough layer at a bulk density of 1.3 g/cm³ weighs 3 900 t/ha, so 1 mg/kg is 3.9 kg/ha.
 */
export const KG_HA_PER_MG_KG = 3.9

/**
 * The starting values as the form asks for them: `max` is in the form's unit, and `perApiUnit` turns the form's number
 * into the API's (N, P and K: mg/kg to kg/ha; 500 mg/kg stays under the API's 2000 kg/ha).
 */
export const STARTING_VALUES = [
  { key: 'initialNKg', label: 'Starting nitrogen (N)', unit: 'mg/kg', max: 500, perApiUnit: KG_HA_PER_MG_KG, hint: 'Plant-available N from a soil test (0–30 cm).' },
  { key: 'initialPKg', label: 'Starting phosphorus (P)', unit: 'mg/kg', max: 500, perApiUnit: KG_HA_PER_MG_KG, hint: 'Plant-available P from a soil test (0–30 cm).' },
  { key: 'initialKKg', label: 'Starting potassium (K)', unit: 'mg/kg', max: 500, perApiUnit: KG_HA_PER_MG_KG, hint: 'Plant-available K from a soil test (0–30 cm).' },
  { key: 'initialDepletionMm', label: 'Starting water deficit', unit: 'mm', max: 1000, perApiUnit: 1, hint: 'Water missing from the root zone. 0 is fully wet. Overrides the drought risk.' },
] as const satisfies readonly { key: keyof FieldConditions; label: string; unit: string; max: number; perApiUnit: number; hint: string }[]

/** An API value (kg/ha, mm) in the form's unit, rounded to two decimals so a typed 12.5 mg/kg comes back as 12.5. */
export const toFormUnit = (value: number | null, perApiUnit: number) => value === null ? null : Math.round((value / perApiUnit) * 100) / 100
/** A form value in the API's unit. */
export const toApiUnit = (value: number | null, perApiUnit: number) => value === null ? null : Math.round(value * perApiUnit * 1000) / 1000

/** A typed starting value as the API takes it: blank is unknown, anything else must be a number from 0 to the limit. */
export function parseStartingValue(text: string, max: number): { value: number | null } | { error: string } {
  const trimmed = text.trim().replace(',', '.')
  if (!trimmed) return { value: null }
  const value = Number(trimmed)
  return Number.isFinite(value) && value >= 0 && value <= max ? { value } : { error: `Enter 0 to ${max}, or leave it empty.` }
}

export const DROUGHT_LEVELS = [
  // Stored as the API's class names; "High" is the farmer-facing word for drought_prone.
  { value: 'drought_prone', label: 'High' }, { value: 'moderate', label: 'Moderate' }, { value: 'low', label: 'Low' },
] as const
export const SOIL_DRAINAGES = [
  { value: 'well_drained', label: 'Well drained' }, { value: 'moderate', label: 'Moderately drained' }, { value: 'poor', label: 'Poorly drained' },
] as const
export const SOIL_TEXTURES = [
  { value: 'sand', label: 'Sand' }, { value: 'loamy_sand', label: 'Loamy sand' }, { value: 'sandy_loam', label: 'Sandy loam' },
  { value: 'loam', label: 'Loam' }, { value: 'silt_loam', label: 'Silt loam' }, { value: 'clay_loam', label: 'Clay loam' },
  { value: 'clay', label: 'Clay' }, { value: 'peat', label: 'Peat' },
] as const

const labelOf = (options: readonly { value: string; label: string }[], value: string | null) => options.find(option => option.value === value)?.label ?? null

/** The field's conditions as they travel with a save: missing ones (an older API answer) are unknown. */
export const conditionsOf = (field: Partial<FieldConditions>): FieldConditions => ({
  droughtLevel: field.droughtLevel ?? null, soilDrainage: field.soilDrainage ?? null, soilTexture: field.soilTexture ?? null,
  initialNKg: field.initialNKg ?? null, initialPKg: field.initialPKg ?? null, initialKKg: field.initialKKg ?? null,
  initialDepletionMm: field.initialDepletionMm ?? null,
})

/**
 * "High drought risk · Loam · Well drained · N 30 · P 10 · K 50 mg/kg · 30 mm water deficit": the known conditions in
 * reading order, empty when none are known. N, P and K are shown in the form's mg/kg.
 */
export function conditionsText(conditions: Partial<FieldConditions>): string {
  const { droughtLevel, soilDrainage, soilTexture, initialNKg, initialPKg, initialKKg, initialDepletionMm } = conditionsOf(conditions)
  const drought = labelOf(DROUGHT_LEVELS, droughtLevel)
  const nutrients = ([['N', initialNKg], ['P', initialPKg], ['K', initialKKg]] as const)
    .filter(([, value]) => value !== null).map(([symbol, value]) => `${symbol} ${toFormUnit(value, KG_HA_PER_MG_KG)}`)
  return [drought && `${drought} drought risk`, labelOf(SOIL_TEXTURES, soilTexture), labelOf(SOIL_DRAINAGES, soilDrainage),
    nutrients.length ? `${nutrients.join(' · ')} mg/kg` : null,
    initialDepletionMm !== null ? `${initialDepletionMm} mm water deficit` : null].filter(Boolean).join(' · ')
}
