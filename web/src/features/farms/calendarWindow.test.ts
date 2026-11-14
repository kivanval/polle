import { describe, expect, it } from 'vitest'
import { canExtendEarlier, canExtendLater, extendEarlier, extendLater, monthsBetween, openingWindow, yearWindow, MONTHS_BEFORE, WINDOW_MONTHS } from './calendarWindow'

const first = new Date(1950, 0, 1), last = new Date(2056, 11, 31)
const month = (date: Date) => `${date.getFullYear()}-${date.getMonth() + 1}`

describe('vertical calendar window', () => {
  it('opens a few months before the anchor, so it can scroll down to it', () => {
    const window = openingWindow(new Date(2026, 9, 17), first, last)
    expect(month(window.start)).toBe('2026-7')
    expect(window.count).toBe(WINDOW_MONTHS)
    expect(monthsBetween(window.start, new Date(2026, 9, 1))).toBe(MONTHS_BEFORE)
  })

  it('stays inside the months that can be picked', () => {
    const harvest = openingWindow(new Date(2026, 4, 1), new Date(2026, 4, 12), last)
    expect(month(harvest.start)).toBe('2026-5')
    const late = openingWindow(new Date(2056, 10, 1), first, last)
    expect(month(late.start)).toBe('2056-8')
    expect(late.count).toBe(5)
    expect(canExtendLater(late, last)).toBe(false)
  })

  it('adds a year above and below without moving the months already shown', () => {
    const window = openingWindow(new Date(2026, 9, 1), first, last)
    const earlier = extendEarlier(window, first)
    expect(month(earlier.start)).toBe('2025-7')
    expect(monthsBetween(earlier.start, window.start) + window.count).toBe(earlier.count)
    const later = extendLater(window, last)
    expect(later.start).toEqual(window.start)
    expect(later.count).toBe(window.count + 12)
  })

  it('stops extending at the first month allowed', () => {
    const window = openingWindow(new Date(1950, 1, 1), first, last)
    expect(month(window.start)).toBe('1950-1')
    expect(canExtendEarlier(window, first)).toBe(false)
    expect(extendEarlier(window, first)).toEqual(window)
  })

  it('jumps to January of a chosen year', () => {
    const window = yearWindow(2031, first, last)
    expect(month(window.start)).toBe('2031-1')
    expect(window.count).toBeGreaterThanOrEqual(12)
  })
})
