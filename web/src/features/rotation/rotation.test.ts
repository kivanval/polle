import { describe, expect, it } from 'vitest'
import type { Field } from '@/features/farms/api'
import type { Crop, Planting } from '@/features/farms/plantings'
import { fieldPlan, planBars, rotationSummary } from './rotation'

const field = (id: string, soilAvailable = true, parentFieldId: string | null = null): Field => ({
  id, farmId: 'farm', name: id, description: null, parentFieldId, areaHectares: 10, soilAvailable, createdAtUtc: '', updatedAtUtc: '',
  boundary: { type: 'Polygon', coordinates: [[[0, 0], [1, 0], [1, 1], [0, 0]]] },
})

const planting = (id: string, fieldId: string, status: Planting['status'], plantingDate: string, harvestingDate: string | null, cropId = 1): Planting => ({
  id, fieldId, status, plantingDate, harvestingDate, cropId, cropProfileId: cropId * 10, seasonYear: Number(plantingDate.slice(0, 4)),
  notes: null, createdAtUtc: '', updatedAtUtc: '',
})

const crops: Crop[] = [
  { id: 1, name: 'Wheat', nameUk: 'Wheat (uk)', scientificName: null, profiles: [] },
  { id: 2, name: 'Soybean', nameUk: 'Soybean (uk)', scientificName: null, profiles: [] },
]

describe('fieldPlan', () => {
  it('reports the crop growing now, the next planned one and how many lie ahead', () => {
    const plan = fieldPlan([
      planting('a', 'f', 'historical', '2024-04-01', '2024-09-01'),
      planting('b', 'f', 'current', '2026-04-01', '2026-12-01'),
      planting('d', 'f', 'planned', '2028-04-01', '2028-09-01', 2),
      planting('c', 'f', 'planned', '2027-04-01', '2027-09-01', 2),
    ])

    expect(plan.growing?.id).toBe('b')
    expect(plan.next?.id).toBe('c')
    expect(plan.ahead).toBe(2)
  })

  it('has nothing ahead for a field whose crops are all in the past', () => {
    const plan = fieldPlan([planting('a', 'f', 'historical', '2024-04-01', '2024-09-01')])

    expect(plan).toEqual({ growing: null, next: null, ahead: 0 })
  })
})

describe('planBars', () => {
  it('draws the crops in sowing order with their crop names and colours, open end kept open', () => {
    const bars = planBars([
      planting('later', 'f', 'planned', '2027-04-01', null, 2),
      planting('first', 'f', 'historical', '2025-04-01', '2025-09-01', 1),
    ], crops)

    expect(bars.map(bar => [bar.key, bar.label, bar.from, bar.to])).toEqual([
      ['first', 'Wheat', '2025-04-01', '2025-09-01'],
      ['later', 'Soybean', '2027-04-01', null],
    ])
    expect(bars[0].colour).toMatch(/^var\(--crop-\d\)$/)
    expect(bars[1].detail).toBe('From Apr 1, 2027')
  })
})

describe('rotationSummary', () => {
  it('counts fields with a plan ahead, fields without one and fields without soil data', () => {
    const fields = [field('planned'), field('growing'), field('bare'), field('sea', false), field('sub', true, 'planned')]
    const plantings = [
      planting('p', 'planned', 'planned', '2027-04-01', '2027-09-01'),
      planting('g', 'growing', 'current', '2026-04-01', '2026-12-01'),
      planting('h', 'bare', 'historical', '2024-04-01', '2024-09-01'),
      planting('q', 'planned', 'planned', '2028-04-01', '2028-09-01'),
    ]

    expect(rotationSummary(fields, plantings)).toEqual({ fields: 5, withPlan: 2, withoutPlan: 3, noSoil: 1, upcoming: 2 })
  })
})
