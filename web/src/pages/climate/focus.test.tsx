import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { DroughtCell, DroughtClass, DroughtPoint, GridLayer, MapLayer } from '@/features/drought/api'
import { ClimateFocusHead } from './ClimateFocusHead'
import { readingBadge, type ReadingBadge } from './focus'

const classes: DroughtClass[] = [
  { code: 'D4', label: 'Exceptional drought', percentile_max: 2, color: '#730000' },
  { code: 'D0', label: 'Abnormally dry', percentile_max: 30, color: '#ffff00' },
]
const drought: GridLayer = { id: 'drought', kind: 'grid', label: 'Drought', description: '', legend: [] }
const temperature: GridLayer = { id: 'temperature', kind: 'grid', label: 'Temperature', description: '', unit: '°C', edges: [0, 20],
  legend: [{ label: 'Cold', color: '#00f' }, { label: 'Mild', color: '#0f0' }, { label: 'Hot', color: '#f00' }] }
const satellite: MapLayer = { id: 'ndvi', kind: 'gibs', label: 'Vegetation', description: '', gibs_layer: '', tile_matrix_set: '', max_native_zoom: 9, legend_url: '', tiles: '' }
const cell = (overrides: Partial<DroughtCell> = {}): DroughtCell => ({ month: '2026-07', status: 'ok', percentile: 45, droughtClass: -1, tmeanC: 24.3,
  tminC: 15, tmaxC: 31, precipitation30dMm: 40, et030dMm: 120, waterBalance30dMm: -80, ...overrides })
const point = (cells: DroughtCell[]): DroughtPoint => ({ status: 'complete', latitude: 50, longitude: 30, cellLatitude: 50.25, cellLongitude: 30.25, months: cells, reason: null })
const read = (data: DroughtPoint) => ({ data, isPending: false, error: null })
const badge = (layer: MapLayer, state: Parameters<typeof readingBadge>[1]) => readingBadge(layer, state, '2026-07', classes, '#cfe8c8')

describe('collapsed card reading', () => {
  it('names the drought class, or No drought above the driest classes', () => {
    expect(badge(drought, read(point([cell()])))).toEqual({ label: 'No drought', color: '#cfe8c8' })
    expect(badge(drought, read(point([cell({ droughtClass: 0 })])))).toEqual({ label: 'Exceptional drought', color: '#730000' })
  })

  it('says a dry season or desert is not rated', () => {
    expect(badge(drought, read(point([cell({ status: 'arid' })]))).label).toBe('Not rated')
  })

  it('gives a weather layer its value in the legend colour', () => {
    expect(badge(temperature, read(point([cell()])))).toEqual({ label: '24 °C', color: '#f00' })
    expect(badge(temperature, read(point([cell({ tmeanC: 12 })]))).color).toBe('#0f0')
  })

  it('shows loading, failure and a missing month plainly', () => {
    expect(badge(drought, { isPending: true, error: null }).label).toBe('Reading…')
    expect(badge(drought, { isPending: false, error: new Error('down') }).label).toBe('No reading')
    expect(badge(drought, read(point([cell({ month: '2026-01' })]))).label).toBe('No value here')
  })

  it('names a satellite layer, whose reading needs its image', () => {
    expect(badge(satellite, { isPending: true, error: null })).toEqual({ label: 'Vegetation', color: null })
  })
})

const picked = { lat: 50.45, lng: 30.52 }
const noDrought: ReadingBadge = { label: 'No drought', color: '#cfe8c8' }
const head = (collapsed: boolean, place: { lat: number; lng: number } | null = picked) => renderToStaticMarkup(<ClimateFocusHead picked={place}
  collapsed={collapsed} badge={noDrought} details={<span>July 2026</span>} onCollapse={() => {}} onClear={() => {}} />)

describe('selected place card head', () => {
  it('open: the title, the month details, then collapse before clear', () => {
    const html = head(false)
    expect(html).toContain('Selected: <span class="mono">50.45° N, 30.52° E</span>')
    expect(html).toContain('July 2026')
    expect(html).not.toContain('climate-focus-badge')
    expect(html.indexOf('aria-label="Collapse the selected place"')).toBeGreaterThan(-1)
    expect(html.indexOf('aria-label="Collapse the selected place"')).toBeLessThan(html.indexOf('aria-label="Clear the selected place"'))
    expect(html).toMatch(/aria-expanded="true"[^>]*aria-controls="climate-focus-body"/)
  })

  it('collapsed: keeps the place and a one-word reading, and both the title and the chevron open it', () => {
    const html = head(true)
    expect(html).toContain('50.45° N, 30.52° E')
    expect(html).toMatch(/class="climate-focus-badge" style="--verdict:#cfe8c8"><span class="climate-focus-badge-swatch" aria-hidden="true"><\/span>No drought</)
    expect(html).not.toContain('July 2026')
    expect(html).toMatch(/<h2><button type="button" class="climate-focus-title"/)
    expect(html).toMatch(/aria-expanded="false"[^>]*aria-label="Expand the selected place"/)
    // Clearing stays available: collapsing never drops the place.
    expect(html).toContain('aria-label="Clear the selected place"')
  })

  it('has nothing to fold or clear before a place is picked', () => {
    const html = head(true, null)
    expect(html).toContain('No place selected')
    expect(html).not.toContain('climate-focus-actions')
    expect(html).not.toContain('climate-focus-badge')
  })
})
