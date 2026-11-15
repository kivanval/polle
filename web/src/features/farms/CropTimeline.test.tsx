import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import type { Field, PolygonGeometry } from './api'
import { CropTimeline } from './CropTimeline'
import { newCropSlot, overlaps } from './periods'
import type { Crop, Planting } from './plantings'

const boundary: PolygonGeometry = { type: 'Polygon', coordinates: [[[30, 50], [30.01, 50], [30.01, 50.01], [30, 50.01], [30, 50]]] }
const field: Field = { id: 'north', farmId: 'farm', name: 'North field', description: null, parentFieldId: null, areaHectares: 12, boundary,
  createdAtUtc: '', updatedAtUtc: '', soilAvailable: true }
const crops: Crop[] = [{ id: 1, name: 'Onion', nameUk: null, scientificName: null, profiles: [{ id: 11, systemName: 'Bulb', isPerennial: false }] }]
const planting = (overrides: Partial<Planting> = {}): Planting => ({ id: 'p1', fieldId: 'north', cropId: 1, cropProfileId: 11, seasonYear: 2027,
  plantingDate: '2027-04-01', harvestingDate: null, notes: null, status: 'planned', createdAtUtc: '', updatedAtUtc: '', ...overrides })
const addButton = (html: string) => html.match(/<button[^>]*>(?:(?!<\/button>).)*Add a crop(?:(?!<\/button>).)*<\/button>/)?.[0] ?? ''

describe('adding a crop', () => {
  it('is always open, even while a crop has no harvest date', () => {
    const button = addButton(renderToStaticMarkup(<CropTimeline field={field} plantings={[planting()]} crops={crops} disabled={false} />))
    expect(button).not.toBe('')
    expect(button).not.toContain('disabled')
    expect(button).not.toContain('Close Onion')
    expect(button).not.toContain('Set a harvest date')
  })

  it('suggests the day after the last harvest', () => {
    expect(newCropSlot([planting({ harvestingDate: '2027-08-20' })]).plantingDate).toBe('2027-08-21')
  })

  it('suggests no day while the last crop is still growing, and a crop sown before it fits only with a harvest date', () => {
    const onion = planting()
    expect(newCropSlot([onion]).plantingDate).toBe('')
    // A 2026 crop ahead of the onion sown in 2027: fine when it is harvested before, a clash while left open.
    expect(overlaps('2026-04-01', '2026-09-30', onion.plantingDate, onion.harvestingDate)).toBe(false)
    expect(overlaps('2026-04-01', null, onion.plantingDate, onion.harvestingDate)).toBe(true)
    expect(overlaps('2027-05-01', '2027-09-30', onion.plantingDate, onion.harvestingDate)).toBe(true)
  })
})
