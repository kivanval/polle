import { describe, expect, it } from 'vitest'
import { arrangeCards, badges, baselineIssue, combineRuns, comparableBaseline, deltas, distinctJobs, evaluationOutdated, groupJobs, partlyEstimated, planFingerprint, plannedLane, reasonText, sortCards, strategyLane, waitingNote, whyText } from './compare'
import { BASELINE_KEY, type StrategyCycle, type StrategyJob, type StrategyResult, type StrategyRun, type StrategySettings } from './api'
import type { Crop, Planting } from '@/features/farms/plantings'

const cycle = (cropName: string, plantingMonth: string, clearanceMonth: string, cropId = 1): StrategyCycle => ({
  cropProfileId: cropId * 10, cropId, cropName, systemName: 'Spring Grain Harvest', plantingMonth, clearanceMonth, harvestMonths: [clearanceMonth],
  revenueUsd: 1000, costsUsd: 400, yieldPercent: 90, irrigationMm: 0, notes: [],
})
const result = (over: Partial<StrategyResult> = {}): StrategyResult => ({
  status: 'OPTIMAL', profitUsd: 1000, revenueUsd: 2000, costsUsd: 1000, irrigationMm: 0, nitrogenBalanceKgHa: 0,
  cycles: [cycle('Wheat', '2027-04', '2027-08')], risks: [], dataGaps: [], ...over,
})
const settings = (irrigation: boolean, fertilizer: boolean, over: Partial<StrategySettings> = {}): StrategySettings =>
  ({ irrigation, fertilizer, allowAgronomicRisks: true, allowSoilDepletion: false, allowMissingData: false, ...over })
const job = (key: string, res: StrategyResult | null, state: StrategyJob['state'] = 'ready'): StrategyJob =>
  ({ key, name: key, summary: '', state, result: res, error: null })

describe('combineRuns', () => {
  const run = (id: string, createdAtUtc: string, jobs: StrategyJob[], startMonth = '2027-01', endMonth = '2031-12'): StrategyRun =>
    ({ id, fieldId: 'field', startMonth, endMonth, createdAtUtc, jobs })
  const plan = result({ profitUsd: 400 })
  const evaluated = run('plan-run', '2026-11-13T10:00:00Z', [job(BASELINE_KEY, plan)])
  const suggested = run('strategies-run', '2026-11-13T11:00:00Z', [job('control', result({ profitUsd: 900 })), job('medium', null, 'processing')])

  it('compares the strategies with the plan when the plan was evaluated first', () => {
    const both = combineRuns(evaluated, suggested)
    expect(both.planJob?.key).toBe(BASELINE_KEY)
    expect(both.planRun?.id).toBe('plan-run')
    expect(both.strategyJobs.map(item => item.key)).toEqual(['control', 'medium'])
    expect(both.baseline).toBe(plan)
    expect(deltas(both.strategyJobs[0].result!, both.baseline)?.profit).toBe(500)
  })

  it('compares them the same way when the strategies were suggested first', () => {
    const later = { ...evaluated, createdAtUtc: '2026-11-13T12:00:00Z' }
    const both = combineRuns(later, suggested)
    expect(both.baseline).toBe(plan)
    expect(both.otherMonths).toBe(false)
    expect(deltas(both.strategyJobs[0].result!, both.baseline)?.profit).toBe(500)
  })

  it('keeps each side when only one button was used', () => {
    expect(combineRuns(undefined, suggested)).toMatchObject({ planJob: undefined, baseline: null, otherMonths: false })
    expect(combineRuns(undefined, suggested).strategyJobs).toHaveLength(2)
    expect(combineRuns(evaluated, undefined)).toMatchObject({ baseline: plan, strategyJobs: [], otherMonths: false })
  })

  it('does not compare with a plan evaluated for other months', () => {
    const elsewhere = run('plan-run', '2026-11-13T12:00:00Z', [job(BASELINE_KEY, plan)], '2028-01', '2032-12')
    const both = combineRuns(elsewhere, suggested)
    expect(both.planJob?.key).toBe(BASELINE_KEY)
    expect(both.baseline).toBeNull()
    expect(both.otherMonths).toBe(true)
  })

  it('does not compare with a plan the solver has not scored yet', () => {
    const pending = run('plan-run', '2026-11-13T12:00:00Z', [job(BASELINE_KEY, null, 'processing')])
    expect(combineRuns(pending, suggested).baseline).toBeNull()
  })

  it('uses the newer plan score of an older or assistant run that scored the plan with the strategies', () => {
    const newerPlan = result({ profitUsd: 100 })
    const combined = run('assistant-run', '2026-11-13T13:00:00Z', [job(BASELINE_KEY, newerPlan), job('control', result())])
    const both = combineRuns(evaluated, combined)
    expect(both.planRun?.id).toBe('assistant-run')
    expect(both.baseline).toBe(newerPlan)
    expect(both.strategyJobs.map(item => item.key)).toEqual(['control'])
    expect(combineRuns({ ...evaluated, createdAtUtc: '2026-11-13T14:00:00Z' }, combined).planRun?.id).toBe('plan-run')
  })
})

describe('deltas', () => {
  it('is the difference from the current plan', () => {
    const d = deltas(result({ profitUsd: 553000, costsUsd: 284000, irrigationMm: 610, risks: ['a', 'b'] }),
      result({ profitUsd: 412000, costsUsd: 188000, irrigationMm: 0, risks: ['a'] }))!
    expect(d).toEqual({ profit: 141000, costs: 96000, irrigation: 610, risks: 1, warnings: 0 })
  })

  it('is null without a baseline', () => {
    expect(deltas(result(), null)).toBeNull()
  })

  it('has no profit difference when a profit is unknown', () => {
    expect(deltas(result({ profitUsd: null }), result())!.profit).toBeNull()
  })
})

describe('arrangeCards', () => {
  const infeasible = result({ status: 'INFEASIBLE', profitUsd: null, revenueUsd: 0, costsUsd: 0, cycles: [] })

  it('draws the current plan as its own card when the solver found no plan for it, never naming it among the strategies that don’t fit', () => {
    const { shown, noPlan } = arrangeCards([job('current-plan', infeasible), job('full-inputs', result()), job('safe-dryland', infeasible)])
    expect(shown.map(item => item.key)).toEqual(['current-plan', 'full-inputs'])
    expect(noPlan.map(item => item.key)).toEqual(['safe-dryland'])
  })

  it('draws a current plan the solver flagged with its figures, and a failed one with its retry', () => {
    const flagged = result({ status: 'UNKNOWN', risks: ['Frost at sowing'] })
    expect(arrangeCards([job('current-plan', flagged)]).shown.map(item => item.key)).toEqual(['current-plan'])
    const failed = arrangeCards([job('current-plan', null, 'failed'), job('cash-grab', null, 'failed')])
    expect(failed.shown.map(item => item.key)).toEqual(['current-plan'])
    expect(failed.failed.map(item => item.key)).toEqual(['cash-grab'])
  })

  it('keeps pending strategies and usable ones on cards', () => {
    expect(arrangeCards([job('full-inputs', null, 'processing'), job('cash-grab', result())]).shown).toHaveLength(2)
  })
})

describe('evaluationOutdated', () => {
  const run = { createdAtUtc: '2026-11-15T10:00:00Z' }
  const planting = (createdAtUtc: string, updatedAtUtc = createdAtUtc) => ({ createdAtUtc, updatedAtUtc })

  it('is outdated once a crop was added or changed after the evaluation', () => {
    expect(evaluationOutdated(run, [planting('2026-11-01T00:00:00Z'), planting('2026-11-15T10:05:00Z')])).toBe(true)
    expect(evaluationOutdated(run, [planting('2026-11-01T00:00:00Z', '2026-11-15T11:00:00.123456Z')])).toBe(true)
  })

  it('is current when every crop is older, and reads timestamps without a zone as UTC', () => {
    expect(evaluationOutdated(run, [planting('2026-11-15T09:59:59Z')])).toBe(false)
    expect(evaluationOutdated({ createdAtUtc: '2026-11-15T10:00:00' }, [planting('2026-11-15T09:00:00Z')])).toBe(false)
    expect(evaluationOutdated(undefined, [planting('2026-11-15T11:00:00Z')])).toBe(false)
  })
})

describe('planFingerprint', () => {
  const crop = (id: string, updatedAtUtc = '2026-11-01T00:00:00Z') => ({ id, updatedAtUtc })

  it('stays the same for the same crops in any order', () => {
    expect(planFingerprint([crop('a'), crop('b')])).toBe(planFingerprint([crop('b'), crop('a')]))
  })

  it('changes when a crop is removed, added or edited', () => {
    const before = planFingerprint([crop('a'), crop('b')])
    expect(planFingerprint([crop('a')])).not.toBe(before)
    expect(planFingerprint([crop('a'), crop('b'), crop('c')])).not.toBe(before)
    expect(planFingerprint([crop('a'), crop('b', '2026-11-15T10:00:00Z')])).not.toBe(before)
  })
})

describe('current plan', () => {
  const infeasible = result({ status: 'INFEASIBLE', profitUsd: null, cycles: [] })

  it('compares strategies only with a plan the solver sent crops back for', () => {
    expect(comparableBaseline(job('current-plan', infeasible))).toBeNull()
    expect(comparableBaseline(job('current-plan', null, 'processing'))).toBeNull()
    expect(comparableBaseline(undefined)).toBeNull()
    const scored = result({ status: 'UNKNOWN' })
    expect(comparableBaseline(job('current-plan', scored))).toBe(scored)
  })

  it('explains why the solver did not accept the plan', () => {
    expect(baselineIssue(job('current-plan', infeasible))).toMatch(/^This plan breaks one of the planner’s rules \(nutrient balance, water, crop stage lengths or rotation\), so it couldn’t be scored\./)
    // Short and plain: two sentences, no catalog or weather jargon.
    const estimated = baselineIssue({ ...job('current-plan', result({})), estimated: true })!
    expect(estimated.split('. ').length).toBeLessThanOrEqual(2)
    expect(estimated).toMatch(/rough estimate/)
    expect(estimated).not.toMatch(/catalog|averaged/)
    expect(baselineIssue(job('current-plan', result({ risks: ['Frost'] })))).toBeNull()
    expect(baselineIssue(job('current-plan', null, 'processing'))).toBeNull()
  })

  it('says when the solver scored only some crops and the rest are estimated, and still compares with the plan', () => {
    const partial = job('current-plan', result({ status: 'FEASIBLE', cycles: [
      cycle('Wheat', '2027-10', '2028-07'), cycle('Sunflower', '2028-09', '2029-01', 2), { ...cycle('Peas', '2029-04', '2029-07', 3), estimated: true },
    ] }))
    expect(partlyEstimated(partial)).toBe(true)
    expect(baselineIssue(partial)).toMatch(/^Some crops break the planner’s rules/)
    expect(comparableBaseline(partial)).toBe(partial.result)
    const whole = job('current-plan', result({ status: 'FEASIBLE' }))
    expect(partlyEstimated(whole)).toBe(false)
    expect(baselineIssue(whole)).toBeNull()
  })

  it('keeps an unscored current plan in the comparison through its estimated audit, and says so', () => {
    const audit = result({ status: 'ESTIMATED', risks: ['Drought: rain covers only 60%'] })
    const estimated = { ...job('current-plan', audit), estimated: true }
    expect(comparableBaseline(estimated)).toBe(audit)
    expect(baselineIssue(estimated)).toMatch(/so it couldn’t be scored\. Below is our rough estimate instead\.$/)
    expect(arrangeCards([estimated]).shown).toEqual([estimated])
  })

  it('draws the farmer\'s own crops sown inside the horizon, in order', () => {
    const planting = (id: string, plantingDate: string, harvestingDate: string | null, cropId = 1): Planting => ({
      id, cropId, cropProfileId: cropId * 10, seasonYear: Number(plantingDate.slice(0, 4)), plantingDate, harvestingDate, fieldId: 'f', notes: null,
      status: 'planned', createdAtUtc: '', updatedAtUtc: '' })
    const crops = [{ id: 1, name: 'Wheat', nameUk: null, scientificName: null, profiles: [] }] satisfies Crop[]
    const lane = plannedLane([planting('b', '2028-04-01', null), planting('a', '2027-04-10', '2027-08-20'), planting('old', '2025-04-01', '2025-08-01'),
      planting('late', '2031-01-01', null)], crops, '2026-12', '2030-12', '2026-11-13')
    expect(lane.map(bar => bar.key)).toEqual(['a', 'b'])
    expect(lane[0]).toMatchObject({ label: 'Wheat', from: '2027-04-10', to: '2027-08-20', open: false, detail: '10 Apr 2027 – 20 Aug 2027' })
    expect(lane[1]).toMatchObject({ to: '2028-12-31', open: true, detail: '1 Apr 2028 – no harvest date' })
  })
})

describe('distinctJobs', () => {
  it('keeps one of identical plans, the stricter one earlier in the run', () => {
    const same = result({ profitUsd: 1_106_039 })
    const kept = distinctJobs([job('current-plan', same), job('full-inputs', same), job('full-inputs-estimates', same), job('cash-grab', result({ profitUsd: 656_854 }))])
    expect(kept.map(item => item.key)).toEqual(['current-plan', 'full-inputs', 'cash-grab'])
  })

  it('keeps plans that differ only in a crop', () => {
    const kept = distinctJobs([job('bold-dryland', result()), job('safe-dryland', result({ cycles: [cycle('Barley', '2027-04', '2027-08', 2)] }))])
    expect(kept).toHaveLength(2)
  })

  it('never drops pending, failed or empty cards', () => {
    const empty = result({ status: 'INFEASIBLE', profitUsd: null, cycles: [] })
    const kept = distinctJobs([job('bold-dryland', null, 'processing'), job('balanced-dryland', null, 'processing'), job('safe-dryland', empty), job('safe-fertilized', empty)])
    expect(kept).toHaveLength(4)
  })
})

describe('groupJobs', () => {
  it('groups identical plans under the first, strictest card and lists the others as its variations', () => {
    const same = result({ profitUsd: 1_106_039 })
    const { cards, variations } = groupJobs([job('current-plan', same), job('medium', same), job('full-inputs', same), job('cash-grab', same),
      job('control', result({ profitUsd: 5 }))])
    expect(cards.map(card => card.key)).toEqual(['current-plan', 'medium', 'control'])
    expect(variations['medium'].map(item => item.key)).toEqual(['full-inputs', 'cash-grab'])
    expect(variations['control']).toBeUndefined()
  })

  it('groups the farmer\'s own strategies with a preset that came out with the same plan, like any other', () => {
    const same = result({ profitUsd: 1_106_039 })
    const { cards, variations } = groupJobs([job('medium', same), job('custom-010-ter-qua', same), job('custom-000-ter-qua', same)])
    expect(cards.map(card => card.key)).toEqual(['medium'])
    expect(variations['medium'].map(item => item.key)).toEqual(['custom-010-ter-qua', 'custom-000-ter-qua'])
  })
})

describe('whyText', () => {
  it('opens with the risk stance, never with data completeness', () => {
    const plan = result({ cycles: [cycle('Corn', '2027-04', '2027-09')] })
    expect(whyText(plan, null, settings(false, true, { allowAgronomicRisks: false }))).toBe('Safe plan. Fertilizer lifts yields without paying for water.')
    expect(whyText(plan, null, settings(false, false))).toMatch(/^Accepts agronomic risks\. /)
    expect(whyText(plan, null, settings(true, true))).not.toMatch(/data/i)
  })

  it('says when missing figures were estimated', () => {
    expect(whyText(result(), null, settings(true, true, { allowMissingData: true }))).toMatch(/crops with missing figures are planned from estimates/i)
  })

  it('gives just the reason when the run has no settings (an older API)', () => {
    expect(whyText(result(), null, undefined)).toBe('Best result the planner found for these limits.')
  })
})

describe('badges', () => {
  it('picks the winners among strategies', () => {
    const won = badges([
      job('current-plan', result({ profitUsd: 9_999_999, irrigationMm: 0, costsUsd: 1 })),
      job('bold-irrigated', result({ profitUsd: 553000, irrigationMm: 610, costsUsd: 300, risks: ['x', 'y'] })),
      job('safe-irrigated', result({ profitUsd: 470000, irrigationMm: 280, costsUsd: 250, risks: [] })),
      job('safe-dryland', result({ profitUsd: 390000, irrigationMm: 0, costsUsd: 120, risks: ['x'] })),
    ])
    expect(won['bold-irrigated']).toEqual(['Highest profit'])
    expect(won['safe-irrigated']).toEqual(['Fewest risks'])
    expect(won['safe-dryland']).toEqual(['Least water', 'Lowest cost'])
    expect(won['current-plan']).toBeUndefined()
  })

  it('never gives an infeasible card a badge', () => {
    const won = badges([
      job('bold-irrigated', result({ profitUsd: 500, irrigationMm: 100 })),
      job('safe-dryland', result({ status: 'INFEASIBLE', profitUsd: null, irrigationMm: 0, costsUsd: 0, cycles: [] })),
    ])
    expect(won['safe-dryland']).toBeUndefined()
  })

  it('gives tied cards the same badge', () => {
    const won = badges([job('bold-dryland', result({ irrigationMm: 0 })), job('safe-dryland', result({ irrigationMm: 0 }))])
    expect(won['bold-dryland']).toContain('Least water')
    expect(won['safe-dryland']).toContain('Least water')
  })
})

describe('sortCards', () => {
  it('keeps the current plan first, then sorts and puts pending and failed last', () => {
    const order = sortCards([
      job('failed-one', null, 'failed'),
      job('low', result({ profitUsd: 100 })),
      job('pending', null, 'processing'),
      job('current-plan', result({ profitUsd: 50 })),
      job('high', result({ profitUsd: 900 })),
    ], 'profit').map(card => card.key)
    expect(order).toEqual(['current-plan', 'high', 'low', 'pending', 'failed-one'])
  })

  it('sorts by least water ascending', () => {
    expect(sortCards([job('wet', result({ irrigationMm: 500 })), job('dry', result({ irrigationMm: 0 }))], 'water').map(card => card.key))
      .toEqual(['dry', 'wet'])
  })
})

describe('reasonText', () => {
  it('explains fertilizer without irrigation', () => {
    expect(reasonText(result({ cycles: [cycle('Corn', '2027-04', '2027-09')] }), null, settings(false, true))).toBe('Fertilizer lifts yields without paying for water.')
  })

  it('mentions legumes', () => {
    const text = reasonText(result({ cycles: [cycle('Peas', '2027-04', '2027-08'), cycle('Wheat', '2027-10', '2028-07'), cycle('Soybean', '2029-05', '2029-09')] }), null, settings(false, false))
    expect(text).toContain('Peas and Soybean add nitrogen to the soil')
    expect(text.endsWith('.')).toBe(true)
    expect(text[0]).toBe(text[0].toUpperCase())
  })

  it('explains irrigation and bare field time', () => {
    const baseline = result({ cycles: [cycle('Wheat', '2027-04', '2027-08')] })
    const text = reasonText(result({ irrigationMm: 300, cycles: [cycle('Corn', '2027-04', '2027-09'), cycle('Wheat', '2027-10', '2028-07')] }), baseline, settings(true, true))
    expect(text.toLowerCase()).toContain('irrigation closes the water gap')
    expect(text).toContain('the field is bare less often')
  })

  it('falls back to a plain sentence', () => {
    expect(reasonText(result({ cycles: [cycle('Corn', '2027-04', '2027-09')] }), null, settings(true, true)))
      .toBe('Best result the planner found for these limits.')
  })
})

describe('strategyLane', () => {
  it('turns cycles into timeline bars over whole months', () => {
    const [bar] = strategyLane(result({ cycles: [cycle('Wheat', '2026-10', '2027-07', 7726)] }))
    expect(bar).toMatchObject({ label: 'Wheat', from: '2026-10-01', to: '2027-07-31' })
    expect(bar.detail).toContain('Oct 2026')
  })

  it('labels a cycle with the solver’s sowing and clearance days when it gave them', () => {
    const [bar] = strategyLane(result({ cycles: [{ ...cycle('Wheat', '2026-10', '2027-07', 7726), plantingDate: '2026-10-11', clearanceDate: '2027-07-21' }] }))
    expect(bar.detail).toBe('Spring Grain Harvest, 11 Oct 2026 – 21 Jul 2027')
  })
})

describe('waitingNote', () => {
  const waiting = (key: string, flag = true) => ({ ...job(key, null), waiting: flag })

  it('says the planner has not taken the farmer’s plan yet', () => {
    expect(waitingNote([waiting('current-plan')], 'plan')).toBe('The planner hasn’t taken your plan yet. Sending it again.')
  })

  it('counts the suggested plans the planner has not taken yet', () => {
    expect(waitingNote([waiting('control'), waiting('medium'), waiting('cash', false)], 'plans'))
      .toBe('The planner hasn’t taken 2 of them yet. Sending them again.')
    expect(waitingNote([waiting('control')], 'plans')).toBe('The planner hasn’t taken 1 of them yet. Sending it again.')
  })

  it('says nothing when every job was taken, or from an API without the flag', () => {
    expect(waitingNote([waiting('control', false)], 'plans')).toBeNull()
    expect(waitingNote([job('control', null)], 'plans')).toBeNull()
  })
})
