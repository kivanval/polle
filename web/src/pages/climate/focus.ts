import type { DroughtCell, DroughtClass, DroughtPoint, GridLayer, MapLayer } from '@/features/drought/api'
import { celsius, mm } from '@/features/drought/format'

/** The model's weather value a grid layer maps; drought is read by its own reading. */
export const gridValue = (layer: GridLayer, cell: DroughtCell) => layer.id === 'temperature' ? cell.tmeanC : layer.id === 'rain' ? cell.precipitation30dMm : null

/** The legend class a value falls in: below edges[0] is the first, at or above the last edge the last. */
export function gridClass(layer: GridLayer, value: number | null) {
  if (value === null || !layer.edges?.length) return null
  const index = layer.edges.filter(edge => value >= edge).length
  return layer.legend[Math.min(index, layer.legend.length - 1)] ?? null
}

export const gridText = (layer: GridLayer, value: number | null) => layer.id === 'temperature' ? celsius(value) : mm(value)

/** The place's point reading as the card has it: still loading, failed, or read. */
export type PointState = { data?: DroughtPoint; isPending: boolean; error: Error | null }

export type ReadingBadge = { label: string; color: string | null }

/**
 * What a collapsed card says about the place in one short badge: the drought class ("No drought"), a weather layer's
 * value, or the satellite layer's name (its reading needs the image, which the collapsed card doesn't load).
 */
export function readingBadge(layer: MapLayer, point: PointState, month: string, classes: DroughtClass[], noDroughtColor: string | null): ReadingBadge {
  if (layer.kind !== 'grid') return { label: layer.label, color: null }
  if (point.error) return { label: 'No reading', color: null }
  if (point.isPending || !point.data) return { label: 'Reading…', color: null }
  const cell = point.data.status === 'complete' ? point.data.months.find(item => item.month === month) : undefined
  if (!cell) return { label: 'No value here', color: null }
  if (layer.id !== 'drought') {
    const value = gridValue(layer, cell)
    return { label: gridText(layer, value), color: gridClass(layer, value)?.color ?? null }
  }
  if (cell.status !== 'ok') return { label: 'Not rated', color: null }
  const level = cell.droughtClass >= 0 ? classes[cell.droughtClass] ?? null : null
  return level ? { label: level.label, color: level.color } : { label: 'No drought', color: noDroughtColor }
}

export const coordinates = (lat: number, lng: number) => `${Math.abs(lat).toFixed(2)}° ${lat >= 0 ? 'N' : 'S'}, ${Math.abs(lng).toFixed(2)}° ${lng >= 0 ? 'E' : 'W'}`
