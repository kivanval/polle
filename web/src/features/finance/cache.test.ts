import { describe, expect, it } from 'vitest'
import type { Expense } from './api'
import { withSavedExpense } from './cache'

const expense = (id: number, spentOn: string, amountUsd = 10): Expense => ({
  id, farmId: 'farm', spentOn, category: 'fuel', description: `Line ${id}`, quantity: null, unit: null, unitPriceUsd: null, amountUsd,
  supplier: null, documentNumber: null, isPaid: true, fieldId: null, workerId: null, equipmentId: null, notes: null,
  createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: '2026-01-01T00:00:00Z',
})

describe('withSavedExpense', () => {
  const ledger = [expense(2, '2026-05-01'), expense(1, '2026-03-01')]

  it('adds a new line in date order, newest first', () => {
    expect(withSavedExpense(ledger, expense(3, '2026-04-01'), 2026).map(line => line.id)).toEqual([2, 3, 1])
  })

  it('puts a same-day line with the higher id first, as the API does', () => {
    expect(withSavedExpense(ledger, expense(3, '2026-05-01'), 2026).map(line => line.id)).toEqual([3, 2, 1])
  })

  it('replaces an edited line instead of duplicating it', () => {
    const next = withSavedExpense(ledger, expense(1, '2026-03-01', 99), 2026)
    expect(next).toHaveLength(2)
    expect(next.find(line => line.id === 1)?.amountUsd).toBe(99)
  })

  it('drops a line moved to another year and leaves other years alone', () => {
    expect(withSavedExpense(ledger, expense(1, '2025-12-31'), 2026).map(line => line.id)).toEqual([2])
    expect(withSavedExpense([], expense(1, '2026-03-01'), 2025)).toEqual([])
  })
})
