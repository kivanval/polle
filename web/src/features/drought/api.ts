import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/shared/api/client'
import { env } from '@/shared/config/env'

/** Fitted: inside the trend's fit years. Projected: the trend carried forward. */
export type MonthKind = 'fitted' | 'projected'
export type DroughtClass = { code: string; label: string; percentile_max: number; color: string }
export type ClimateMonth = { month: string; kind: MonthKind }
export type LegendItem = { label: string; color: string }
/** Painted by the API from the trend model for every month of the timeline. */
/** `edges` split the values into the legend's classes: values below edges[0] take legend[0], and so on. */
export type GridLayer = { id: string; kind: 'grid'; label: string; description: string; unit?: string; legend: LegendItem[]; edges?: number[] }
/** NASA GIBS satellite tiles of real observations; months not long past show the newest image. */
export type SatelliteLayer = {
  id: string; kind: 'gibs'; label: string; description: string; gibs_layer: string; tile_matrix_set: string
  max_native_zoom: number; legend_url: string; tiles: string
}
export type CdiLayer = { id: 'cdi'; kind: 'wms'; label: string; description: string; legend_url: string }
export type MapLayer = GridLayer | SatelliteLayer | CdiLayer
export type DroughtOverview = {
  generatedAtUtc: string; fitYears: [number, number]; projectedFrom: string
  imageBounds: [[number, number], [number, number]]; layers: (GridLayer | SatelliteLayer)[]
  index: { name: string; window_days: number; base_period: string }; method: string
  classes: DroughtClass[]; noDrought: { label: string; color: string } | null; kinds: Record<MonthKind, string>
  sources: { name: string; url: string }[]; months: ClimateMonth[]
}
export type DroughtCell = {
  month: string; status: 'ok' | 'arid' | 'no_data' | 'polar'; percentile: number | null; droughtClass: number
  tmeanC: number | null; tminC: number | null; tmaxC: number | null
  precipitation30dMm: number | null; et030dMm: number | null; waterBalance30dMm: number | null
}
/** Every month of the timeline for one grid cell. */
export type DroughtPoint = {
  status: 'complete' | 'unavailable'; latitude: number; longitude: number
  cellLatitude: number | null; cellLongitude: number | null; months: DroughtCell[]; reason: string | null
}

export const droughtKeys = {
  overview: ['drought'] as const,
  point: (latitude: number, longitude: number) => ['drought', 'point', latitude, longitude] as const,
}

export function useDroughtOverview(enabled = true) {
  return useQuery({ queryKey: droughtKeys.overview, queryFn: () => apiFetch<DroughtOverview>('/drought'), staleTime: 60 * 60_000, enabled })
}

export function useDroughtPoint(point: { lat: number; lng: number } | null) {
  return useQuery({
    queryKey: droughtKeys.point(point?.lat ?? 0, point?.lng ?? 0),
    queryFn: () => apiFetch<DroughtPoint>(`/drought/point?latitude=${point!.lat}&longitude=${point!.lng}`),
    staleTime: 60 * 60_000,
    enabled: Boolean(point),
  })
}

/** The model timestamp versions the URL, so a new model bypasses the browser cache. */
export const layerImageUrl = (overview: DroughtOverview, layer: string, month: string) =>
  `${env.apiBaseUrl}/drought/${month}/layers/${layer}.png?v=${encodeURIComponent(overview.generatedAtUtc)}`

export const copernicusWms = 'https://drought.emergency.copernicus.eu/api/wms'

/** Every switchable layer: the model's grid layers and NASA satellite layers, then Copernicus CDI. */
export function mapLayers(overview: DroughtOverview): MapLayer[] {
  return [...overview.layers, {
    id: 'cdi', kind: 'wms', label: 'Copernicus drought (Europe)',
    description: 'Official Combined Drought Indicator from rainfall, soil moisture and satellite vegetation. Europe only, 10-day periods about a month behind.',
    legend_url: `${copernicusWms}?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetLegendGraphic&LAYER=cdiad&FORMAT=image/png`,
  }]
}

export type LayerThemeId = 'drought' | 'weather' | 'satellite'
/** A topic on the map and the datasets that can show it. Datasets of one theme stay separate choices. */
export type LayerTheme = { id: LayerThemeId; label: string; layers: MapLayer[] }
/** Where a dataset comes from and what it covers, as stated by the model manifest and the providers. */
export type LayerSource = { name: string; provider: string; coverage: string; cadence: string }

export const themeOf = (layer: MapLayer): LayerThemeId =>
  layer.id === 'drought' || layer.kind === 'wms' ? 'drought' : layer.kind === 'grid' ? 'weather' : 'satellite'
const themeLabels: Record<LayerThemeId, string> = { drought: 'Drought', weather: 'Weather', satellite: 'Land from orbit' }

export function layerThemes(layers: MapLayer[]): LayerTheme[] {
  return (Object.keys(themeLabels) as LayerThemeId[])
    .map(id => ({ id, label: themeLabels[id], layers: layers.filter(layer => themeOf(layer) === id) }))
    .filter(theme => theme.layers.length > 0)
}

export function layerSource(layer: MapLayer, overview: DroughtOverview): LayerSource {
  if (layer.kind === 'wms') return {
    name: 'Combined Drought Indicator', provider: 'Copernicus European Drought Observatory',
    coverage: 'Europe only', cadence: '10-day periods, about a month behind',
  }
  if (layer.kind === 'gibs') return { name: layer.label, provider: 'NASA GIBS', coverage: 'Global', cadence: 'Observed images. Recent months show the newest' }
  // Grid layers come from the Polle trend model, fitted to the station datasets listed in its manifest.
  const inputs = overview.sources.filter(source => !source.name.includes('GIBS')).map(source => source.name.replace(/ \(.*\)$/, ''))
  return {
    // The provider line already names the Polle trend model, so the title is the layer's own, as on the other tabs.
    name: layer.id === 'drought' ? 'Water-balance trend' : layer.label,
    provider: inputs.length ? `Polle trend model, from ${inputs.join(' · ')}` : 'Polle trend model',
    coverage: 'Worldwide land, 0.5° grid (about 50 km)',
    cadence: `Monthly, fitted ${overview.fitYears[0]}–${overview.fitYears[1]} and projected`,
  }
}

// Satellite products are published weeks after they are observed.
const SATELLITE_LAG_DAYS = 45

/**
 * The image a satellite layer shows for a month: the one from the middle of the month once the month is
 * well past, else the newest one (time null). CDI uses the 10-day period that contains the 15th.
 */
export function satelliteTime(layer: SatelliteLayer | CdiLayer, month: string, today = new Date()): { time: string | null; held: boolean } {
  const settled = new Date(today.getTime() - SATELLITE_LAG_DAYS * 86_400_000).toISOString().slice(0, 10)
  const middle = `${month}-15`
  if (middle > settled) return { time: null, held: true }
  return { time: layer.kind === 'wms' ? `${month}-11` : middle, held: false }
}

export const currentMonth = (today = new Date()) => today.toISOString().slice(0, 7)

/** The month the map opens on: the current one when it is in the timeline, else the nearest end. */
export function defaultMonth(overview: DroughtOverview) {
  const { months } = overview
  const now = currentMonth()
  if (months.some(item => item.month === now)) return now
  return now < months[0].month ? months[0].month : months[months.length - 1].month
}

/** The same calendar month in every year from `from` on, e.g. each July ahead. */
export function sameMonthAhead(months: DroughtCell[], month: string, from: string) {
  const calendarMonth = month.slice(5)
  return months.filter(item => item.month.slice(5) === calendarMonth && item.month.slice(0, 4) >= from.slice(0, 4))
}
