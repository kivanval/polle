import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { ApiError } from '@/shared/api/client'
import type { Field, PolygonGeometry } from './api'
import { PlantingModal } from './PlantingModal'
import type { Crop, Fertilizer, Planting } from './plantings'

const boundary: PolygonGeometry = { type: 'Polygon', coordinates: [[[30, 50], [30.01, 50], [30.01, 50.01], [30, 50.01], [30, 50]]] }
const field: Field = { id: 'north', farmId: 'farm', name: 'North field', description: null, parentFieldId: null, areaHectares: 12, boundary,
  createdAtUtc: '', updatedAtUtc: '', soilAvailable: true }
const crops: Crop[] = [{ id: 1, name: 'Wheat', nameUk: null, scientificName: null, profiles: [{ id: 11, systemName: 'Winter grain', isPerennial: false }] }]
const catalog: Fertilizer[] = [
  { id: 2215, name: 'Ammonium nitrate', nitrogenPercent: 34.4, phosphatePercent: 0, potashPercent: 0 },
  { id: 2216, name: 'NPK 16-16-16', nitrogenPercent: 16, phosphatePercent: 16, potashPercent: 16 },
]
const planting = (overrides: Partial<Planting> = {}): Planting => ({ id: 'p1', fieldId: 'north', cropId: 1, cropProfileId: 11, seasonYear: 2027,
  plantingDate: '2026-10-01', harvestingDate: '2027-07-20', notes: null, status: 'planned', createdAtUtc: '', updatedAtUtc: '', ...overrides })

function render(setup: (client: QueryClient) => void, existing?: Planting) {
  // retryOnMount off: a failed list would otherwise render as loading again, before its retry.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, retryOnMount: false } } })
  setup(client)
  return renderToStaticMarkup(<QueryClientProvider client={client}>
    <PlantingModal field={field} crops={crops} periods={existing ? [existing] : []} planting={existing} onClose={() => {}} />
  </QueryClientProvider>)
}
const fertilizerSelect = (html: string) => html.match(/<select id="fertilizer-[^"]*"[^>]*>(.*?)<\/select>/)?.[0] ?? ''

describe('crop form fertilizer', () => {
  it('lists every product from the catalog after None, by its name', () => {
    const select = fertilizerSelect(render(client => client.setQueryData(['fertilizers'], catalog)))
    expect(select).not.toContain('disabled')
    expect([...select.matchAll(/<option value="([^"]*)"[^>]*>([^<]*)</g)].map(match => [match[1], match[2]]))
      .toEqual([['', 'None'], ['2215', 'Ammonium nitrate'], ['2216', 'NPK 16-16-16']])
  })

  it('shows the rate from the start, so the form does not shift, and enables it once a product is chosen', () => {
    const none = render(client => client.setQueryData(['fertilizers'], catalog))
    expect(none).toMatch(/<input id="fertilizer-rate-new"[^>]*disabled=""/)

    const chosen = render(client => client.setQueryData(['fertilizers'], catalog), planting({ fertilizerMaterialId: 2216, fertilizerRateKgHa: 250 }))
    expect(fertilizerSelect(chosen)).toMatch(/<option value="2216" selected="">NPK 16-16-16</)
    expect(chosen).toMatch(/<input id="fertilizer-rate-p1"[^>]*value="250"/)
    expect(chosen).toContain('About 3,000 kg of the product for the whole field (12 ha).')
  })

  it('keeps a saved product the catalog no longer lists, so saving does not drop it', () => {
    const html = render(client => client.setQueryData(['fertilizers'], catalog), planting({ fertilizerMaterialId: 99, fertilizerRateKgHa: 100 }))
    expect(fertilizerSelect(html)).toMatch(/<option value="99" selected="">Product 99</)
  })

  it('says why the list is missing and offers to load it again', () => {
    const html = render(client => client.getQueryCache().build(client, { queryKey: ['fertilizers'] }).setState({
      status: 'error', fetchStatus: 'idle', error: new ApiError(500, { status: 500, detail: 'Something went wrong on our server. Please try again shortly.' }),
    }))
    expect(html).toContain('Something went wrong on our server. Please try again shortly.')
    expect(html).toMatch(/<button type="button" class="text-link"[^>]*>Try again<\/button>/)
  })
})

describe('crop form irrigation hint', () => {
  it('sits under its own input, as a hint with the shared hint style', () => {
    const html = render(client => client.setQueryData(['fertilizers'], catalog))
    expect(html).toMatch(/<input id="irrigation-new"[^>]*\/><p id="irrigation-hint-new" class="crop-form-hint">1 mm is 10 m³ of water per hectare\.<\/p>/)
  })
})

describe('crop form target levels', () => {
  const html = (existing: Planting) => render(client => client.setQueryData(['fertilizers'], catalog), existing)
  const input = (markup: string, name: string) => markup.match(new RegExp(`<input id="${name}"[^>]*>`))?.[0] ?? ''

  it('lays out every input from the start, in order, so choosing water or a fertilizer moves nothing', () => {
    const empty = html(planting())
    const order = ['irrigation-p1', 'irrigation-target-p1', 'fertilizer-p1', 'fertilizer-rate-p1', 'fertilizer-target-p1'].map(name => empty.indexOf(`id="${name}"`))
    expect(order.every(position => position >= 0)).toBe(true)
    expect([...order].sort((a, b) => a - b)).toEqual(order)
  })

  it('enables the refill level only once the crop is irrigated, and shows its saved value', () => {
    expect(input(html(planting()), 'irrigation-target-p1')).toContain('disabled=""')
    const watered = input(html(planting({ irrigationMm: 140, irrigationTargetPercent: 60 })), 'irrigation-target-p1')
    expect(watered).not.toContain('disabled=""')
    expect(watered).toContain('value="60"')
  })

  it('enables the nutrient level only once a product is chosen, and shows its saved value', () => {
    expect(input(html(planting()), 'fertilizer-target-p1')).toContain('disabled=""')
    const fed = input(html(planting({ fertilizerMaterialId: 2216, fertilizerRateKgHa: 250, fertilizerTargetPercent: 75 })), 'fertilizer-target-p1')
    expect(fed).not.toContain('disabled=""')
    expect(fed).toContain('value="75"')
  })

  it('leaves the levels empty for a crop without them, so the planner uses its default', () => {
    const empty = html(planting({ irrigationMm: 140, fertilizerMaterialId: 2216, fertilizerRateKgHa: 250 }))
    expect(input(empty, 'irrigation-target-p1')).toContain('value=""')
    expect(input(empty, 'fertilizer-target-p1')).toContain('value=""')
  })
})
