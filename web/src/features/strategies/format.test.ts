import { describe, expect, it } from 'vitest'
import type { StrategyCycle } from './api'
import { categoryText, cubicMetres, cycleNotes, cycleRisks, cycleWarnings, dateText, defaultHorizon, harvestDays, noteLines, signedCubicMetres, waterVolume,
  whenText } from './format'
import { yearOptions } from './month'

describe('dates to the day', () => {
  it('reads a solver day as day month year and a month as month year', () => {
    expect(dateText('2027-05-11')).toBe('11 May 2027')
    expect(dateText('2027-04-21')).toBe('21 Apr 2027')
    expect(dateText('2027-04-01')).toBe('1 Apr 2027')
    expect(dateText('2027-05')).toBe('May 2027')
  })

  it('shows the day when the solver gave one and the month for older results', () => {
    expect(whenText('2027-05-11', '2027-05')).toBe('11 May 2027')
    expect(whenText(null, '2027-05')).toBe('May 2027')
    expect(whenText(undefined, '2027-05')).toBe('May 2027')
    expect(whenText('2027-05', '2027-05')).toBe('May 2027')
  })

  it('lists harvest days in order, or the months when there are none', () => {
    expect(harvestDays({ harvestMonths: ['2028-07', '2027-07'], harvestDates: ['2028-07-21', '2027-07-11'] })).toEqual(['2027-07-11', '2028-07-21'])
    expect(harvestDays({ harvestMonths: ['2028-07', '2027-07'], harvestDates: [] })).toEqual(['2027-07', '2028-07'])
    expect(harvestDays({ harvestMonths: ['2027-07'] })).toEqual(['2027-07'])
  })
})

describe('default horizon', () => {
  it('runs from next month through December five years later', () => {
    expect(defaultHorizon(new Date(Date.UTC(2026, 10, 9)))).toEqual({ startMonth: '2026-12', endMonth: '2031-12' })
  })

  it('rolls into the next year in December', () => {
    expect(defaultHorizon(new Date(Date.UTC(2026, 11, 15)))).toEqual({ startMonth: '2027-01', endMonth: '2032-12' })
  })

  it('ends inside the years the form offers', () => {
    for (let month = 0; month < 12; month++) {
      const today = new Date(Date.UTC(2026, month, 1))
      const { startMonth, endMonth } = defaultHorizon(today)
      const months = (Number(endMonth.slice(0, 4)) - Number(startMonth.slice(0, 4))) * 12 + Number(endMonth.slice(5)) - Number(startMonth.slice(5))
      expect(months).toBeLessThan(72)
      expect(yearOptions(today.getUTCFullYear())).toContain(Number(endMonth.slice(0, 4)))
    }
  })
})

describe('water volume', () => {
  it('turns millimetres over a field into cubic metres (1 mm on 1 ha = 10 m³)', () => {
    expect(waterVolume(1, 1)).toBe(10)
    expect(waterVolume(120.5, 48)).toBeCloseTo(57840, 6)
  })

  it('formats whole cubic metres with thousands separators', () => {
    expect(cubicMetres(120.5, 48)).toBe('57,840 m³')
    expect(cubicMetres(0, 48)).toBe('0 m³')
  })

  it('signs a difference', () => {
    expect(signedCubicMetres(610, 48)).toBe('+292,800 m³')
    expect(signedCubicMetres(-5, 2)).toBe('−100 m³')
    expect(signedCubicMetres(0, 2)).toBe('±0 m³')
  })
})

describe('note lines', () => {
  it('keeps each note on its own line', () => {
    expect(noteLines(['Soil pH too high (7.8 > 7.5)', 'Severe drought shock in stage \'flowering\'']))
      .toEqual(['Soil pH too high (7.8 > 7.5)', 'Severe drought shock in stage \'flowering\''])
  })

  it('splits a note that packs several messages with semicolons', () => {
    expect(noteLines(['Soil pH too high; Marginal soil pH ;Heat shock'])).toEqual(['Soil pH too high', 'Marginal soil pH', 'Heat shock'])
  })

  it('drops blanks and repeats', () => {
    expect(noteLines(['', ' ; ', 'Soil pH too high', 'Soil pH too high;'])).toEqual(['Soil pH too high'])
    expect(noteLines([])).toEqual([])
  })
})

describe('risks, warnings and notes', () => {
  const base = { cropProfileId: 1, cropId: 1, cropName: 'Wheat', systemName: '', plantingMonth: '2027-10', clearanceMonth: '2028-07',
    harvestMonths: ['2028-07'], revenueUsd: 0, costsUsd: 0, yieldPercent: 100, irrigationMm: 0 } satisfies Partial<StrategyCycle>

  it('keeps the three apart when the API splits them', () => {
    const cycle: StrategyCycle = { ...base, notes: ['Sown late'], risks: ['Frost; hail'], warnings: ['No salinity data'] }
    expect(cycleRisks(cycle)).toEqual(['Frost', 'hail'])
    expect(cycleWarnings(cycle)).toEqual(['No salinity data'])
    expect(cycleNotes(cycle)).toEqual(['Sown late'])
  })

  it('reads every note as a risk from an API older than the split', () => {
    const cycle: StrategyCycle = { ...base, notes: ['Frost'] }
    expect(cycleRisks(cycle)).toEqual(['Frost'])
    expect(cycleWarnings(cycle)).toEqual([])
    expect(cycleNotes(cycle)).toEqual([])
  })

  it('accepts flags it does not know yet, and names any new note category', () => {
    const cycle: StrategyCycle = { ...base, notes: [], risks: [], warnings: [], soilHealth: true, nitrogenValue: 42 }
    expect(cycleRisks(cycle)).toEqual([])
    expect(categoryText('soil_health')).toBe('Soil health')
    expect(categoryText('')).toBe('')
  })
})
