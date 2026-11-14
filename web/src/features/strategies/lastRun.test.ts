import { describe, expect, it } from 'vitest'
import { forgetLastPlanRun, forgetLastRun, readLastPlanRun, readLastRun, rememberLastPlanRun, rememberLastRun } from './lastRun'

const memory = () => {
  const items = new Map<string, string>()
  return { getItem: (key: string) => items.get(key) ?? null, setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key) }
}
const RUN = '3f2b8c1e-9a4d-4e6f-8b2a-1c3d5e7f9a0b'

describe('last strategy run', () => {
  it('remembers the run per field', () => {
    const store = memory()
    rememberLastRun('field-a', RUN, store)
    expect(readLastRun('field-a', store)).toBe(RUN)
    expect(readLastRun('field-b', store)).toBeNull()
  })

  it('keeps the plan evaluation apart from the strategies, so neither run replaces the other', () => {
    const store = memory()
    const PLAN = '0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d'
    rememberLastRun('field-a', RUN, store)
    rememberLastPlanRun('field-a', PLAN, store)
    expect(readLastRun('field-a', store)).toBe(RUN)
    expect(readLastPlanRun('field-a', store)).toBe(PLAN)
    forgetLastPlanRun('field-a', store)
    expect(readLastPlanRun('field-a', store)).toBeNull()
    expect(readLastRun('field-a', store)).toBe(RUN)
  })

  it('forgets a run', () => {
    const store = memory()
    rememberLastRun('field-a', RUN, store)
    forgetLastRun('field-a', store)
    expect(readLastRun('field-a', store)).toBeNull()
  })

  it('ignores values that are not run ids', () => {
    const store = memory()
    store.setItem('polle.strategy-run.field-a', 'not-a-guid')
    expect(readLastRun('field-a', store)).toBeNull()
  })

  it('works without storage, or when storage throws', () => {
    const broken = { getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') }, removeItem: () => { throw new Error('blocked') } }
    expect(readLastRun('field-a', null)).toBeNull()
    expect(readLastRun('field-a', broken)).toBeNull()
    expect(() => rememberLastRun('field-a', RUN, broken)).not.toThrow()
    expect(() => forgetLastRun('field-a', broken)).not.toThrow()
  })
})
