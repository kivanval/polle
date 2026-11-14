import { describe, expect, it } from 'vitest'
import type { DroughtCell, DroughtPoint } from './api'
import { percentileBands, scalePosition, yearMonths } from './reading'

const cell = (month: string, over: Partial<DroughtCell> = {}): DroughtCell => ({
  month, status: 'ok', percentile: 20, droughtClass: -1, tmeanC: 10, tminC: 4, tmaxC: 16, precipitation30dMm: 50, et030dMm: 60, waterBalance30dMm: -10, ...over,
})
const point = (months: DroughtCell[]): DroughtPoint => ({ status: 'complete', latitude: 0, longitude: 0, cellLatitude: 0, cellLongitude: 0, months, reason: null })

describe('scalePosition', () => {
  it('places a value on its scale and holds it inside', () => {
    expect(scalePosition(0, -200, 200)).toBe(50)
    expect(scalePosition(500, 0, 300)).toBe(100)
    expect(scalePosition(-40, -30, 45)).toBe(0)
    expect(scalePosition(5, 1, 1)).toBe(0)
  })
})

describe('percentileBands', () => {
  it('cuts the 0–50 scale into the classes, driest first, with the rest as no drought', () => {
    const bands = percentileBands([
      { code: 'D1', label: 'Moderate', percentile_max: 20, color: '#b' },
      { code: 'D0', label: 'Abnormally dry', percentile_max: 30, color: '#a' },
      { code: 'D4', label: 'Exceptional', percentile_max: 2, color: '#e' },
    ], '#n')
    expect(bands.map(band => [band.label, band.from, band.to])).toEqual([
      ['Exceptional', 0, 4], ['Moderate', 4, 40], ['Abnormally dry', 40, 60], ['No drought', 60, 100]])
    expect(bands.at(-1)!.color).toBe('#n')
  })
})

describe('yearMonths', () => {
  it('keeps the months of the chosen year', () => {
    expect(yearMonths(point([cell('2026-12'), cell('2027-01'), cell('2027-02')]), '2027-05').map(item => item.month)).toEqual(['2027-01', '2027-02'])
  })
})
