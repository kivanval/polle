import { describe, expect, it } from 'vitest'
import { barLayout, cropColour, monthRange, openEnd, timelineRange } from './timeline'

const range = { start: '2026-01-01', end: '2031-12-31' }

describe('barLayout', () => {
  it('places a bar as a percent of the range', () => {
    const { left, width } = barLayout(range, '2026-04-01', '2026-09-30')
    expect(left).toBeCloseTo(4.11, 1)
    expect(width).toBeCloseTo(8.35, 1)
  })

  it('runs an open-ended bar to the range end', () => {
    const { left, width } = barLayout(range, '2029-04-10', null)
    expect(left + width).toBeCloseTo(100, 6)
  })

  it('clamps bars that start before the range', () => {
    const { left, width } = barLayout(range, '2024-05-01', '2026-03-31')
    expect(left).toBe(0)
    expect(width).toBeCloseTo(4.11, 1)
  })
})

describe('timelineRange', () => {
  it('covers this year through five years ahead when there are no plantings', () => {
    expect(timelineRange([], '2026-11-08')).toEqual({ start: '2026-01-01', end: '2031-12-31' })
  })

  it('reaches back to the first planting and forward to the last', () => {
    expect(timelineRange([{ plantingDate: '2024-04-01', harvestingDate: '2024-09-01' }, { plantingDate: '2033-04-01', harvestingDate: null }], '2026-11-08'))
      .toEqual({ start: '2024-01-01', end: '2033-12-31' })
  })
})

describe('monthRange', () => {
  it('turns year-months into the first and last day', () => {
    expect(monthRange('2028-02', '2028-02')).toEqual({ from: '2028-02-01', to: '2028-02-29' })
  })
})

describe('cropColour', () => {
  it('is stable for a crop', () => {
    expect(cropColour(7726)).toBe(cropColour(7726))
    expect(cropColour(7726)).toMatch(/^var\(--crop-\d\)$/)
  })
})

describe('openEnd', () => {
  it('ends a crop without a harvest date with its season, not years later', () => {
    expect(openEnd({ plantingDate: '2027-03-01', seasonYear: 2027 }, '2026-11-09')).toBe('2027-12-31')
  })

  it('keeps a crop still growing past its season visible until today', () => {
    expect(openEnd({ plantingDate: '2025-10-01', seasonYear: 2026 }, '2027-02-10')).toBe('2027-02-10')
  })

  it('never ends before the crop is sown', () => {
    expect(openEnd({ plantingDate: '2028-04-01', seasonYear: 2027 }, '2026-11-09')).toBe('2028-12-31')
  })
})
