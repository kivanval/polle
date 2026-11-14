import type { CatalogColumn, CatalogOption, RowValues } from './api'

const UNITS: [RegExp, string][] = [
  [/_usd_per_ha$/, 'USD/ha'], [/_usd_per_tonne$/, 'USD/t'], [/_cost_per_hour_usd$|_rate_usd$/, 'USD/h'], [/_usd$/, 'USD'],
  [/^hours_per_ha$|_hours_per_ha$/, 'h/ha'], [/_per_ha$/, 'per ha'], [/_temp_c$/, '°C'], [/_mm$/, 'mm'], [/_gdd$|_gdd_interval$/, 'GDD'],
  [/_days$/, 'days'], [/_years$/, 'years'], [/_months_gap$/, 'months'],
]
const WORDS: Record<string, string> = { gdd: 'GDD', usd: 'USD', ha: 'ha', id: 'ID' }

/** "absolute_min_temp_c" → { label: "Absolute min temp", unit: "°C" }; "crop_id" → "Crop". */
export function columnLabel(name: string): { label: string; unit: string | null } {
  const unit = UNITS.find(([pattern]) => pattern.test(name))?.[1] ?? null
  let words = name.replace(/_id$/, '').replace(/_(c|mm|usd|usd_per_ha|per_ha)$/, '').split('_')
  if (name === 'id') words = ['ID']
  const label = words.map((word, index) => WORDS[word] ?? (index === 0 ? word.charAt(0).toUpperCase() + word.slice(1) : word)).join(' ')
  return { label, unit }
}

/** `clip`: show one line cut with an ellipsis, so a long list stays inside its column; the full text goes in a tooltip. */
export type Cell = { text: string; sub?: string; muted?: boolean; numeric?: boolean; clip?: boolean }

const isPrimitive = (item: unknown) => item === null || ['string', 'number', 'boolean'].includes(typeof item)

/** A list such as allowed soil textures reads as "sand, loam, …" instead of one unbreakable JSON string. */
function structured(value: object): Cell {
  const text = Array.isArray(value) && value.every(isPrimitive) ? value.map(item => String(item)).join(', ') : JSON.stringify(value)
  return { text: text.length > 300 ? `${text.slice(0, 300)}…` : text, muted: true, clip: true }
}

export function cell(column: CatalogColumn, value: unknown, labels?: Map<string, string>): Cell {
  if (value === null || value === undefined) return { text: '—', muted: true }
  if (column.link && labels) {
    const label = labels.get(String(value))
    return label ? { text: label, sub: `#${value}` } : { text: `#${value}`, muted: true }
  }
  switch (column.kind) {
    case 'translations': {
      const names = value as Record<string, string>
      const others = Object.entries(names).filter(([lang]) => lang !== 'en').map(([lang, text]) => `${lang}: ${text}`)
      return { text: names.en ?? Object.values(names)[0] ?? '—', sub: others.join(' · ') || undefined }
    }
    case 'boolean': return { text: value ? 'Yes' : 'No' }
    case 'integer': case 'decimal': return { text: String(value), numeric: true }
    case 'json': return structured(value as object)
    default: {
      // Postgres arrays (an unsupported column kind) arrive as JSON arrays too.
      if (typeof value === 'object') return structured(value)
      const text = String(value)
      // A long value without spaces, e.g. "sand,loamy_sand,sandy_loam", cannot wrap and would spill into the next column.
      return /\S{40,}/.test(text) ? { text, clip: true } : { text }
    }
  }
}

export const optionLabels = (options: CatalogOption[] | undefined) => new Map((options ?? []).map(option => [String(option.key), option.label]))

/** A readable name for a logged row: its English name, system name, description or key. */
export function rowName(values: RowValues | null, key: RowValues): string {
  const name = values?.name
  if (name && typeof name === 'object') return (name as Record<string, string>).en ?? Object.values(name as Record<string, string>)[0]
  for (const field of ['system_name', 'name', 'description', 'slug']) if (typeof values?.[field] === 'string' && values[field]) return values[field] as string
  return Object.entries(key).map(([column, value]) => `${column} ${value}`).join(', ')
}

const show = (value: unknown) => (value === null || value === undefined ? '—' : typeof value === 'object' ? JSON.stringify(value) : String(value))

/** Fields that differ between the logged before and after rows. */
export const changedFields = (before: RowValues | null, after: RowValues | null) =>
  Object.keys({ ...before, ...after })
    .filter(field => JSON.stringify(before?.[field]) !== JSON.stringify(after?.[field]))
    .map(field => ({ field, from: show(before?.[field]), to: show(after?.[field]) }))

const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
export function ago(iso: string, now = Date.now()) {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000)
  const steps: [number, Intl.RelativeTimeFormatUnit][] = [[60, 'second'], [60, 'minute'], [24, 'hour'], [7, 'day'], [4.35, 'week'], [12, 'month']]
  let value = seconds
  for (const [size, unit] of steps) { if (Math.abs(value) < size) return relative.format(Math.round(value), unit); value /= size }
  return relative.format(Math.round(value), 'year')
}
