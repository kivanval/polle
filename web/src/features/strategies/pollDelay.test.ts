import { describe, expect, it } from 'vitest'
import { pollDelay, type StrategyJob, type StrategyRun } from './api'

const run = (...states: StrategyJob['state'][]): StrategyRun => ({ id: 'r', fieldId: 'f', startMonth: '2026-12', endMonth: '2031-12', createdAtUtc: '',
  jobs: states.map((state, index) => ({ key: `k${index}`, name: 'Plan', summary: '', state, result: null, error: null })) })

describe('strategy run polling', () => {
  it('checks every second at first, so a plan the solver scores at once shows at once', () => {
    expect(pollDelay(run('processing'), 1)).toBe(1000)
    expect(pollDelay(run('ready', 'processing'), 14)).toBe(1000)
  })

  it('slows down for a run still processing after the first checks', () => {
    expect(pollDelay(run('processing'), 15)).toBe(5000)
  })

  it('stops once nothing is processing', () => {
    expect(pollDelay(run('ready', 'failed'), 1)).toBe(false)
    expect(pollDelay(undefined, 0)).toBe(false)
  })
})
