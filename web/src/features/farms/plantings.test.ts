import { describe, expect, it } from 'vitest'
import { keptTargets, type Planting } from './plantings'

const copied: Planting = { id: 'p1', fieldId: 'north', cropId: 1, cropProfileId: 11, seasonYear: 2027, plantingDate: '2027-04-11',
  harvestingDate: '2027-08-21', notes: null, status: 'planned', createdAtUtc: '', updatedAtUtc: '',
  irrigationMm: 140.3, irrigationTargetPercent: 60, fertilizerMaterialId: 3101, fertilizerRateKgHa: 180, fertilizerTargetPercent: 75 }

describe('keptTargets', () => {
  it('keeps a crop’s target levels while its water and fertilizer stay as copied', () => {
    expect(keptTargets(copied, 140.3, 3101, 180)).toEqual({ irrigationTargetPercent: 60, fertilizerTargetPercent: 75 })
  })

  it('drops a level once its input changes or is removed', () => {
    expect(keptTargets(copied, 200, 3101, 150)).toEqual({ irrigationTargetPercent: null, fertilizerTargetPercent: null })
    expect(keptTargets(copied, null, null, null)).toEqual({ irrigationTargetPercent: null, fertilizerTargetPercent: null })
    expect(keptTargets(copied, 140.3, 2216, 180)).toEqual({ irrigationTargetPercent: 60, fertilizerTargetPercent: null })
  })

  it('has no levels for a new crop', () => {
    expect(keptTargets(undefined, 80, 3101, 100)).toEqual({ irrigationTargetPercent: null, fertilizerTargetPercent: null })
  })
})
