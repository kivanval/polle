import { describe, expect, it } from 'vitest'
import type { StrategyPreset } from './api'
import { DEFAULT_CHOICES, NEW_CUSTOM, choiceCount, choicesKey, customDescription, customName, presetMatches, readChoices, requestChoices, writeChoices } from './choices'

const PRESETS = ['control', 'rainfed-potential', 'medium', 'full-inputs', 'full-inputs-estimates', 'cash-grab']

describe('requestChoices', () => {
  it('leaves the choice to the API by default, so it runs all its presets', () => {
    expect(requestChoices(DEFAULT_CHOICES, PRESETS)).toBeUndefined()
    expect(choiceCount(DEFAULT_CHOICES, PRESETS)).toBe(6)
  })

  it('sends the chosen presets in the API order, then the custom strategies', () => {
    const choices = { presets: ['cash-grab', 'control'], custom: [NEW_CUSTOM] }
    expect(requestChoices(choices, PRESETS)).toEqual([{ preset: 'control' }, { preset: 'cash-grab' }, NEW_CUSTOM])
    expect(choiceCount(choices, PRESETS)).toBe(3)
  })

  it('runs every preset plus custom ones when only custom strategies were added', () => {
    expect(requestChoices({ presets: null, custom: [NEW_CUSTOM] }, PRESETS)).toHaveLength(7)
  })

  it('sends six presets and a custom strategy (even one with a preset\'s settings) as seven, with its name and description', () => {
    const custom = { ...NEW_CUSTOM, name: '  My dryland test ', description: 'Medium inputs,\n tried on the north field.' }
    const sent = requestChoices({ presets: null, custom: [custom] }, PRESETS)!
    expect(sent).toHaveLength(7)
    expect(choiceCount({ presets: null, custom: [custom] }, PRESETS)).toBe(7)
    expect(sent.slice(0, 6)).toEqual(PRESETS.map(preset => ({ preset })))
    expect(sent[6]).toEqual({ ...NEW_CUSTOM, name: 'My dryland test', description: 'Medium inputs, tried on the north field.' })
  })

  it('sends exactly the chosen mix: two presets and one custom strategy', () => {
    const sent = requestChoices({ presets: ['medium', 'control'], custom: [{ ...NEW_CUSTOM, irrigation: 'OFF' }] }, PRESETS)
    expect(sent).toEqual([{ preset: 'control' }, { preset: 'medium' }, { ...NEW_CUSTOM, irrigation: 'OFF' }])
  })

  it('leaves a blank name and description out, so the API describes the strategy from its settings', () => {
    expect(requestChoices({ presets: [], custom: [{ ...NEW_CUSTOM, name: ' ', description: '' }] }, PRESETS)).toEqual([NEW_CUSTOM])
  })

  it('drops a remembered preset the API no longer offers', () => {
    expect(requestChoices({ presets: ['bold-irrigated', 'medium'], custom: [] }, PRESETS)).toEqual([{ preset: 'medium' }])
  })
})

const memory = () => {
  const store = new Map<string, string>()
  return { store, getItem: (key: string) => store.get(key) ?? null, setItem: (key: string, value: string) => void store.set(key, value),
    removeItem: (key: string) => void store.delete(key) }
}

describe('stored choices', () => {
  it('round-trips through storage', () => {
    const storage = memory()
    writeChoices('user-a', { presets: ['medium'], custom: [NEW_CUSTOM] }, storage)
    expect(readChoices('user-a', storage)).toEqual({ presets: ['medium'], custom: [NEW_CUSTOM] })
  })

  it('keeps each account\'s own strategies apart on one browser', () => {
    const storage = memory()
    const mine = { ...NEW_CUSTOM, name: 'Mine' }, theirs = { ...NEW_CUSTOM, irrigation: 'OFF' as const, name: 'Theirs' }
    writeChoices('user-a', { presets: null, custom: [mine] }, storage)
    expect(readChoices('user-b', storage)).toEqual(DEFAULT_CHOICES)
    writeChoices('user-b', { presets: ['medium'], custom: [theirs] }, storage)
    expect(readChoices('user-a', storage)).toEqual({ presets: null, custom: [mine] })
    expect(readChoices('user-b', storage)).toEqual({ presets: ['medium'], custom: [theirs] })
  })

  it('shows and keeps nothing until the account is known', () => {
    const storage = memory()
    writeChoices('user-a', { presets: null, custom: [NEW_CUSTOM] }, storage)
    expect(readChoices(null, storage)).toEqual(DEFAULT_CHOICES)
    expect(readChoices(undefined, storage)).toEqual(DEFAULT_CHOICES)
    writeChoices(null, { presets: null, custom: [{ ...NEW_CUSTOM, name: 'Lost' }] }, storage)
    expect([...storage.store.keys()]).toEqual([choicesKey('user-a')])
  })

  it('never reads the old browser-wide choices, whose account is unknown, and removes them', () => {
    const storage = memory()
    storage.setItem('ecocrop.strategy-choices.v1', JSON.stringify({ presets: null, custom: [NEW_CUSTOM] }))
    expect(readChoices('user-b', storage)).toEqual(DEFAULT_CHOICES)
    writeChoices('user-b', DEFAULT_CHOICES, storage)
    expect(storage.getItem('ecocrop.strategy-choices.v1')).toBeNull()
  })

  it('falls back to the defaults when storage is empty, broken or blocked', () => {
    expect(readChoices('user-a', { getItem: () => null })).toEqual(DEFAULT_CHOICES)
    expect(readChoices('user-a', { getItem: () => '{not json' })).toEqual(DEFAULT_CHOICES)
    expect(readChoices('user-a', { getItem: () => { throw new Error('blocked') } })).toEqual(DEFAULT_CHOICES)
    const blocked = () => { throw new Error('blocked') }
    expect(() => writeChoices('user-a', DEFAULT_CHOICES, { setItem: blocked, removeItem: blocked })).not.toThrow()
  })

  it('keeps a custom strategy\'s name and description', () => {
    const custom = { ...NEW_CUSTOM, name: 'My dryland test', description: 'Tried on the north field.' }
    const stored = JSON.stringify({ presets: null, custom: [custom, { ...NEW_CUSTOM, name: 42 }] })
    expect(readChoices('user-a', { getItem: () => stored }).custom).toEqual([custom])
  })

  it('skips stored custom strategies with unknown levels', () => {
    const stored = JSON.stringify({ presets: null, custom: [NEW_CUSTOM, { ...NEW_CUSTOM, irrigation: 'LOTS' }] })
    expect(readChoices('user-a', { getItem: () => stored }).custom).toEqual([NEW_CUSTOM])
  })
})

describe('custom strategy wording', () => {
  it('uses the farmer\'s name and description when given', () => {
    const custom = { ...NEW_CUSTOM, name: ' My dryland test ', description: 'Tried on the north field.' }
    expect(customName(custom)).toBe('My dryland test')
    expect(customDescription(custom)).toBe('Tried on the north field.')
  })

  it('reads like the API\'s wording when left blank', () => {
    const custom = { ...NEW_CUSTOM, allowSoilDepletion: true, irrigation: 'OFF' as const, fertilizer: 'CONTINUOUS' as const, name: '' }
    expect(customName(custom)).toBe('Custom: water off, fertilizer dosed finely')
    expect(customDescription(custom)).toBe('Rain only, fertilizer dosed finely. Accepts agronomic risks, may draw the soil down and plans only with complete figures.')
  })

  it('spots a preset with the same settings', () => {
    const medium: StrategyPreset = { key: 'medium', name: 'Medium', summary: '', settings: { irrigation: true, fertilizer: true, allowAgronomicRisks: true,
      allowSoilDepletion: false, allowMissingData: false, irrigationLevel: 'TERNARY', fertilizerLevel: 'QUATERNARY' } }
    expect(presetMatches(medium, NEW_CUSTOM)).toBe(true)
    expect(presetMatches(medium, { ...NEW_CUSTOM, allowMissingData: true })).toBe(false)
  })
})
