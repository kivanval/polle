import { useQuery } from '@tanstack/react-query'
import { copernicusWms, satelliteTime, type CdiLayer, type GridLayer, type SatelliteLayer } from './api'

/**
 * What the map layer itself shows at one place. The Copernicus and NASA services publish images, not values, so the
 * place is read from the colour of the layer's own image there: Copernicus classes from its legend, NASA values from the
 * layer's GIBS colour map, which gives the value behind every colour.
 */

type Rgba = [number, number, number, number]

/** The colour of one pixel of an image another site serves (both allow cross-origin reads). */
async function pixel(url: string, x: number, y: number): Promise<Rgba> {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`The layer's image didn't load (${response.status}).`)
  const bitmap = await createImageBitmap(await response.blob())
  const canvas = document.createElement('canvas')
  canvas.width = bitmap.width; canvas.height = bitmap.height
  const context = canvas.getContext('2d', { willReadFrequently: true })
  if (!context) throw new Error('This browser can’t read the layer’s image.')
  context.drawImage(bitmap, 0, 0)
  const [r, g, b, a] = context.getImageData(Math.min(x, bitmap.width - 1), Math.min(y, bitmap.height - 1), 1, 1).data
  return [r, g, b, a]
}

const distance = (a: readonly number[], b: readonly number[]) => Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]) + Math.abs(a[2] - b[2])

/** Copernicus Combined Drought Indicator classes and recovery classes, with the colours of their WMS legends. */
export const CDI_CLASSES = [
  { rgb: [240, 228, 66], label: 'Watch', detail: 'Rain has been short for a while. Soil and plants are not yet affected.', drought: true },
  { rgb: [230, 159, 0], label: 'Warning', detail: 'Rain is short and soil moisture is below normal.', drought: true },
  { rgb: [220, 5, 12], label: 'Alert', detail: 'Rain and soil moisture are short and vegetation is under stress.', drought: true },
  { rgb: [204, 121, 167], label: 'Soil moisture recovering', detail: 'After a drought, soil moisture is back for now, but vegetation is not yet.', drought: false },
  { rgb: [0, 158, 115], label: 'Vegetation recovering', detail: 'After a drought, vegetation has recovered for now.', drought: false },
  { rgb: [202, 178, 214], label: 'Full recovery', detail: 'The last drought is over: rain, soil and vegetation are back to normal.', drought: false },
  { rgb: [255, 255, 255], label: 'No drought', detail: 'Rain, soil moisture and vegetation are normal for the season.', drought: false },
] as const

export type CdiReading = { label: string; detail: string; color: string | null; drought: boolean; period: string | null }

/** A small image around the place, so the middle pixel is the place's own cell rather than an edge sample. */
function cdiUrl(layers: string, lat: number, lng: number, time: string | null) {
  const half = 0.1
  return `${copernicusWms}?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetMap&LAYERS=${layers}&STYLES=&SRS=EPSG:4326`
    + `&BBOX=${lng - half},${lat - half},${lng + half},${lat + half}&WIDTH=5&HEIGHT=5&FORMAT=image/png&TRANSPARENT=true${time ? `&TIME=${time}` : ''}`
}

const toHex = (rgb: readonly number[]) => `#${rgb.map(part => part.toString(16).padStart(2, '0')).join('')}`

export async function readCdi(lat: number, lng: number, time: string | null): Promise<CdiReading | null> {
  // The drought classes first; where there is none, the recovery layer says normal or recovering.
  for (const layers of ['cdiad', 'cdirc']) {
    const [r, g, b, a] = await pixel(cdiUrl(layers, lat, lng, time), 2, 2)
    if (a < 128) continue
    const match = CDI_CLASSES.reduce((best, item) => distance(item.rgb, [r, g, b]) < distance(best.rgb, [r, g, b]) ? item : best)
    if (distance(match.rgb, [r, g, b]) > 60) continue
    return { label: match.label, detail: match.detail, color: match.label === 'No drought' ? null : toHex(match.rgb), drought: match.drought, period: time }
  }
  return null
}

/** One colour of a GIBS colour map with the value range behind it. */
type ColorEntry = { rgb: [number, number, number]; from: number; to: number; nodata: boolean }
export type GibsColorMap = { units: string; entries: ColorEntry[] }

/** GIBS names a layer's colour map like its legend: …/legends/MODIS_NDVI_H.svg is …/colormaps/v1.3/MODIS_NDVI.xml. */
export const colorMapUrl = (layer: SatelliteLayer) =>
  `https://gibs.earthdata.nasa.gov/colormaps/v1.3/${layer.legend_url.split('/').pop()!.replace(/_[HV]\.svg$/, '')}.xml`

export function parseColorMap(xml: string): GibsColorMap {
  const units = /<ColorMap\b[^>]*\bunits="([^"]*)"/.exec(xml)?.[1] ?? ''
  const attribute = (tag: string, name: string) => new RegExp(`\\b${name}="([^"]*)"`).exec(tag)?.[1]
  const entries = [...xml.matchAll(/<ColorMapEntry\b[^>]*>/g)].flatMap(([tag]) => {
    const rgb = (attribute(tag, 'rgb') ?? '').split(',').map(Number)
    const range = /^[[(]?\s*(-?[\d.]+)\s*(?:,\s*(-?[\d.]+))?\s*[\])]?$/.exec(attribute(tag, 'value') ?? attribute(tag, 'sourceValue') ?? '')
    if (rgb.length !== 3 || rgb.some(Number.isNaN) || !range) return []
    const from = Number(range[1]), to = range[2] === undefined ? from : Number(range[2])
    return [{ rgb: rgb as [number, number, number], from, to, nodata: attribute(tag, 'nodata') === 'true' || attribute(tag, 'transparent') === 'true' }]
  })
  return { units, entries }
}

/** The tile and the pixel in it that hold a place, in Web Mercator at a zoom level. */
export function tilePixel(lat: number, lng: number, zoom: number) {
  const scale = 256 * 2 ** zoom
  const x = (lng + 180) / 360 * scale
  const sin = Math.sin(Math.max(-85.0511, Math.min(85.0511, lat)) * Math.PI / 180)
  const y = (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale
  return { column: Math.floor(x / 256), row: Math.floor(y / 256), x: Math.floor(x % 256), y: Math.floor(y % 256) }
}

export type GibsReading = { value: number; units: string; color: string; date: string | null }

export async function readGibs(layer: SatelliteLayer, lat: number, lng: number, time: string | null, colorMap: GibsColorMap): Promise<GibsReading | null> {
  const zoom = layer.max_native_zoom
  const { column, row, x, y } = tilePixel(lat, lng, zoom)
  const url = layer.tiles.replace('{layer}', layer.gibs_layer).replace('{time}', time ?? 'default').replace('{matrix}', layer.tile_matrix_set)
    .replace('{z}', String(zoom)).replace('{y}', String(row)).replace('{x}', String(column))
  const [r, g, b, a] = await pixel(url, x, y)
  if (a < 128) return null
  const match = colorMap.entries.find(entry => entry.rgb[0] === r && entry.rgb[1] === g && entry.rgb[2] === b)
    ?? colorMap.entries.reduce<ColorEntry | null>((best, entry) => !best || distance(entry.rgb, [r, g, b]) < distance(best.rgb, [r, g, b]) ? entry : best, null)
  if (!match || match.nodata || distance(match.rgb, [r, g, b]) > 12) return null
  return { value: (match.from + match.to) / 2, units: colorMap.units, color: toHex(match.rgb), date: time }
}

/** The value in farm words: NDVI as a share of green cover, kelvin as °C, volumetric soil moisture as percent. */
export function gibsText(reading: GibsReading, layerId: string): { value: string; meaning: string } {
  if (reading.units === 'K') {
    const celsius = reading.value - 273.15
    return { value: `${Math.round(celsius)} °C`, meaning: 'Daytime temperature of the ground surface, which runs hotter than the air on sunny days.' }
  }
  if (reading.units.startsWith('m')) {
    const percent = reading.value * 100
    return { value: `${Math.round(percent)}%`, meaning: `Water in the top metre of soil, by volume. ${percent < 15 ? 'Dry: most crops struggle to draw water.'
      : percent < 30 ? 'Moderate: enough for most crops.' : 'Wet: the soil is close to full.'}` }
  }
  if (layerId === 'ndvi' || reading.units === '') {
    const ndvi = reading.value
    return { value: ndvi.toFixed(2), meaning: ndvi < 0.1 ? 'Bare soil, rock, snow or water: no growing vegetation.'
      : ndvi < 0.3 ? 'Sparse or stressed vegetation, or a field between crops.'
        : ndvi < 0.6 ? 'Moderate vegetation: a crop growing or grassland.' : 'Dense, healthy green vegetation.' }
  }
  return { value: `${reading.value.toFixed(2)} ${reading.units}`, meaning: '' }
}

// A layer's colour map never changes; one fetch per session, retried after a failure.
const colorMaps = new Map<string, Promise<GibsColorMap>>()
function colorMapOf(layer: SatelliteLayer) {
  const url = colorMapUrl(layer)
  if (!colorMaps.has(url)) colorMaps.set(url, fetch(url).then(async response => {
    if (!response.ok) throw new Error(`The layer's colour key didn't load (${response.status}).`)
    return parseColorMap(await response.text())
  }).catch(error => { colorMaps.delete(url); throw error }))
  return colorMaps.get(url)!
}

export type LayerPoint = { kind: 'cdi'; reading: CdiReading | null } | { kind: 'gibs'; reading: GibsReading | null }

/** Reads a Copernicus or NASA layer at the picked place for a month; the model's own layers are read by the point API. */
export function useLayerPoint(layer: GridLayer | SatelliteLayer | CdiLayer, picked: { lat: number; lng: number } | null, month: string) {
  const time = layer.kind === 'grid' ? null : satelliteTime(layer, month).time
  return useQuery({
    queryKey: ['layer-point', layer.id, time, picked?.lat, picked?.lng] as const,
    queryFn: async (): Promise<LayerPoint> => layer.kind === 'wms'
      ? { kind: 'cdi', reading: await readCdi(picked!.lat, picked!.lng, time) }
      : { kind: 'gibs', reading: await readGibs(layer as SatelliteLayer, picked!.lat, picked!.lng, time, await colorMapOf(layer as SatelliteLayer)) },
    enabled: picked !== null && layer.kind !== 'grid',
    staleTime: 10 * 60_000, retry: 1,
  })
}
