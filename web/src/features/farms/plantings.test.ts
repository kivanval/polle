import { describe, expect, it } from 'vitest'
import { targetLevels } from './plantings'

describe('targetLevels', () => {
  it('saves each level the farmer set, with the input it doses', () => {
    expect(targetLevels(140.3, 60, 3101, 75)).toEqual({ irrigationTargetPercent: 60, fertilizerTargetPercent: 75 })
  })

  it('drops a level whose water or fertilizer is gone, since the database needs one with the other', () => {
    expect(targetLevels(null, 60, null, 75)).toEqual({ irrigationTargetPercent: null, fertilizerTargetPercent: null })
    expect(targetLevels(140.3, 60, null, 75)).toEqual({ irrigationTargetPercent: 60, fertilizerTargetPercent: null })
  })

  it('leaves an empty level to the planner', () => {
    expect(targetLevels(80, null, 3101, null)).toEqual({ irrigationTargetPercent: null, fertilizerTargetPercent: null })
  })
})
