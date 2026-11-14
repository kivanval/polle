import { describe, expect, it } from 'vitest'
import { REPLENISHMENT_LEVELS, type CustomStrategy } from './api'
import { NEW_CUSTOM, addHint, customDescription, defaultCustomDescription, defaultCustomName } from './choices'
import { settingsText } from './details'

/** Every combination of a custom strategy's settings. */
function* customs(): Generator<CustomStrategy> {
  for (const irrigation of REPLENISHMENT_LEVELS) for (const fertilizer of REPLENISHMENT_LEVELS)
    for (const allowAgronomicRisks of [false, true]) for (const allowSoilDepletion of [false, true]) for (const allowMissingData of [false, true])
      yield { ...NEW_CUSTOM, irrigation, fertilizer, allowAgronomicRisks, allowSoilDepletion, allowMissingData }
}

describe('strategy copy reads without semicolons', () => {
  it('describes every custom strategy in plain sentences', () => {
    for (const custom of customs()) {
      for (const text of [defaultCustomDescription(custom), defaultCustomName(custom), customDescription(custom),
        ...settingsText({ ...custom, irrigation: custom.irrigation !== 'OFF', fertilizer: custom.fertilizer !== 'OFF', irrigationLevel: custom.irrigation, fertilizerLevel: custom.fertilizer })]) {
        expect(text).not.toContain(';')
      }
    }
  })

  it('starts the second sentence of a description with a capital', () => {
    expect(defaultCustomDescription({ ...NEW_CUSTOM, irrigation: 'TERNARY', fertilizer: 'QUATERNARY', allowAgronomicRisks: true, allowSoilDepletion: false, allowMissingData: false }))
      .toBe('Water in 3 steps, fertilizer in 4 steps. Accepts agronomic risks, keeps the soil fed and plans only with complete figures.')
  })

  it('explains the Add strategy button without semicolons', () => {
    const hints = [addHint({ full: true, duplicate: false }), addHint({ full: false, duplicate: true }),
      addHint({ full: false, duplicate: false, twin: 'Medium' }), addHint({ full: false, duplicate: false })]
    for (const hint of hints) expect(hint).not.toContain(';')
    expect(hints[2]).toBe('Same settings as the Medium preset. It is planned as your own strategy beside it.')
  })
})
