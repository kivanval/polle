import { satelliteTime, type DroughtCell, type DroughtClass, type MapLayer, type MonthKind } from './api'

export const kindLabels: Record<MonthKind, string> = { fitted: 'Trend fit', projected: 'Projection' }

export const formatMonth = (month: string) => new Date(`${month}-01T00:00:00Z`).toLocaleDateString('en-GB', { month: 'short', year: 'numeric', timeZone: 'UTC' })
export const mm = (value: number | null) => value === null ? '—' : `${value.toFixed(0)} mm`
export const celsius = (value: number | null) => value === null ? '—' : `${value.toFixed(0)} °C`
export const percentileText = (value: number | null) => value === null ? '—' : value <= 2 ? '≤ 2nd' : value >= 50 ? '≥ 50th' : `${value.toFixed(0)}th`

/** One plain-language verdict for a cell: the class, or why it has none. */
export function cellVerdict(cell: DroughtCell | null, classes: DroughtClass[], noDroughtColor: string | null = null): { label: string; color: string | null } {
  if (!cell) return { label: 'No data here', color: null }
  if (cell.status === 'arid') return { label: 'Dry season', color: null }
  if (cell.status === 'polar') return { label: 'No growing season', color: null }
  if (cell.status === 'no_data') return { label: 'No data here', color: null }
  const level = cell.droughtClass >= 0 ? classes[cell.droughtClass] : null
  return level ? { label: level.label, color: level.color } : { label: 'No drought', color: noDroughtColor }
}

/** Which satellite image the layer really shows, when that is not the chosen month. */
export function layerDateNote(layer: MapLayer, month: string) {
  if (layer.kind === 'grid') return null
  return satelliteTime(layer, month).held ? 'Satellite images exist only for past months, so the newest one is shown.' : null
}
