import { useId, useState } from 'react'
import { DROUGHT_LEVELS, parseStartingValue, SOIL_DRAINAGES, SOIL_TEXTURES, STARTING_VALUES, toApiUnit, toFormUnit, type FieldConditions } from './conditions'

type StartingKey = (typeof STARTING_VALUES)[number]['key']

/**
 * One starting value as a number box. It keeps what the farmer typed while it is not a number in range yet, and the
 * browser's own min/max check stops the form from saving it; only valid values (or blank) reach the conditions.
 */
function StartingValue({ id, option, value, disabled, onChange }: {
  id: string; option: (typeof STARTING_VALUES)[number]; value: number | null; disabled?: boolean; onChange: (value: number | null) => void
}) {
  // The conditions hold the API's unit; the box shows and takes the form's (mg/kg for N, P and K).
  const [text, setText] = useState(() => { const shown = toFormUnit(value, option.perApiUnit); return shown === null ? '' : String(shown) })
  const parsed = parseStartingValue(text, option.max)
  const error = 'error' in parsed ? parsed.error : null
  return <div className="field-condition">
    <label htmlFor={id}>{option.label} <span className="field-condition-unit">{option.unit}</span></label>
    <input id={id} type="number" inputMode="decimal" min={0} max={option.max} step="any" placeholder="Not sure" value={text} disabled={disabled}
      aria-invalid={error ? true : undefined} aria-describedby={`${id}-hint`}
      onChange={event => {
        setText(event.target.value)
        const next = parseStartingValue(event.target.value, option.max)
        if ('value' in next) onChange(toApiUnit(next.value, option.perApiUnit))
      }} />
    <span id={`${id}-hint`} className={error ? 'field-error' : 'field-condition-hint'}>{error ?? option.hint}</span>
  </div>
}

/**
 * Drought risk, soil texture and drainage as three optional selects, then the soil test's starting N, P, K and water
 * deficit as optional numbers. Strategies use them in place of the estimated soil; "Not sure" leaves the estimate.
 */
export function FieldConditionsFields({ value, disabled, onChange }: {
  value: FieldConditions; disabled?: boolean; onChange: (next: FieldConditions) => void
}) {
  const id = useId()
  const select = (key: keyof FieldConditions, label: string, options: readonly { value: string; label: string }[], hint: string) =>
    <div className="field-condition">
      <label htmlFor={`${id}-${key}`}>{label}</label>
      <select id={`${id}-${key}`} value={value[key] ?? ''} disabled={disabled} aria-describedby={`${id}-${key}-hint`}
        onChange={event => onChange({ ...value, [key]: event.target.value || null })}>
        <option value="">Not sure</option>
        {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
      <span id={`${id}-${key}-hint`} className="field-condition-hint">{hint}</span>
    </div>

  return <div className="field-conditions">
    {select('droughtLevel', 'Drought risk', DROUGHT_LEVELS, 'How dry the field usually is when the season starts.')}
    {select('soilTexture', 'Soil texture', SOIL_TEXTURES, 'How much water the soil holds.')}
    {select('soilDrainage', 'Soil drainage', SOIL_DRAINAGES, 'How fast water leaves after rain.')}
    <fieldset className="field-conditions-start" disabled={disabled}>
      <legend>Soil test at the start <span>(optional)</span></legend>
      <div className="field-conditions-start-grid">
        {STARTING_VALUES.map(option => <StartingValue key={option.key} id={`${id}-${option.key}`} option={option} value={value[option.key as StartingKey]}
          disabled={disabled} onChange={next => onChange({ ...value, [option.key]: next })} />)}
      </div>
    </fieldset>
  </div>
}
