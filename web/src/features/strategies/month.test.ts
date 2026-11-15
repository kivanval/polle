import { describe, expect, it } from 'vitest'
import { joinMonth, splitMonth, yearOptions } from './month'

describe('month fields', () => {
  it('splits and joins YYYY-MM', () => {
    expect(splitMonth('2026-12')).toEqual({ year: 2026, month: 12 })
    expect(joinMonth(2027, 3)).toBe('2027-03')
  })

  it('offers five years back through ten years ahead', () => {
    const years = yearOptions(2026)
    expect(years[0]).toBe(2021)
    expect(years.at(-1)).toBe(2036)
    expect(years).toHaveLength(16)
    expect(years).not.toContain(2037)
  })
})
