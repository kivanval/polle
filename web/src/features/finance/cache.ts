import type { Expense } from './api'

/** Newest first, as GET /expenses returns them. */
const newestFirst = (a: Expense, b: Expense) => b.spentOn.localeCompare(a.spentOn) || b.id - a.id

/**
 * One year's cached ledger after `saved` was created or edited: the line is replaced or added when its date falls in
 * `year`, and dropped when an edit moved it to another year. The list shows the change before the refetch returns.
 */
export function withSavedExpense(expenses: Expense[], saved: Expense, year: number): Expense[] {
  const others = expenses.filter(expense => expense.id !== saved.id)
  return Number(saved.spentOn.slice(0, 4)) === year ? [...others, saved].sort(newestFirst) : others
}
