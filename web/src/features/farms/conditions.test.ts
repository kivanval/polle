import { describe, expect, it } from 'vitest'
import { conditionsOf, conditionsText, KG_HA_PER_MG_KG, NO_CONDITIONS, parseStartingValue, STARTING_VALUES, toApiUnit, toFormUnit } from './conditions'

describe('parseStartingValue', () => {
  it('reads blank as unknown and a number in range as itself, comma decimals too', () => {
    expect(parseStartingValue('  ', 2000)).toEqual({ value: null })
    expect(parseStartingValue('0', 2000)).toEqual({ value: 0 })
    expect(parseStartingValue('45,5', 2000)).toEqual({ value: 45.5 })
    expect(parseStartingValue('1000', 1000)).toEqual({ value: 1000 })
  })

  it('refuses negatives, values over the limit and text', () => {
    for (const text of ['-1', '2000.1', 'abc']) expect(parseStartingValue(text, 2000)).toEqual({ error: 'Enter 0 to 2000, or leave it empty.' })
  })
})

describe('conditions', () => {
  it('treats starting values an older API leaves out as unknown', () => {
    expect(conditionsOf({ droughtLevel: 'low' })).toEqual({ ...NO_CONDITIONS, droughtLevel: 'low' })
  })

  it('describes the soil test after the classes, N-P-K in mg/kg', () => {
    expect(conditionsText({ soilTexture: 'loam', initialNKg: 117, initialKKg: 195, initialDepletionMm: 30 }))
      .toBe('Loam · N 30 · K 50 mg/kg · 30 mm water deficit')
    expect(conditionsText({})).toBe('')
  })
})

describe('starting value units', () => {
  it('takes N, P and K in mg/kg and sends kg/ha for the 0–30 cm layer', () => {
    expect(KG_HA_PER_MG_KG).toBe(3.9)
    expect(toApiUnit(25, KG_HA_PER_MG_KG)).toBe(97.5)
    expect(toApiUnit(null, KG_HA_PER_MG_KG)).toBeNull()
  })

  it('shows a saved value as typed, without float noise', () => {
    for (const typed of [12.34, 25, 0.1, 500]) expect(toFormUnit(toApiUnit(typed, KG_HA_PER_MG_KG), KG_HA_PER_MG_KG)).toBe(typed)
  })

  it('keeps the form limit inside the API limit of 2000 kg/ha', () => {
    for (const option of STARTING_VALUES) expect(option.max * option.perApiUnit).toBeLessThanOrEqual(option.key === 'initialDepletionMm' ? 1000 : 2000)
    expect(STARTING_VALUES.filter(option => option.unit === 'mg/kg').map(option => option.key)).toEqual(['initialNKg', 'initialPKg', 'initialKKg'])
  })
})
