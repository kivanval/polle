import { describe, expect, it } from 'vitest'
import { isStale } from './appVersion'

describe('isStale', () => {
  it('is stale only when the server has another build', () => {
    expect(isStale('a1', 'b2')).toBe(true)
    expect(isStale('a1', 'a1')).toBe(false)
    expect(isStale('a1', null)).toBe(false)
    expect(isStale('dev', 'b2')).toBe(false)
  })
})
