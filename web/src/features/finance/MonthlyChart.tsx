import { useState } from 'react'
import { budgetCategoryLabels, type BudgetCategory } from '@/features/farms/resources'
import type { Expense } from './api'
import { formatCents, formatCompactUsd, sumUsd } from './money'

const MONTHS = Array.from({ length: 12 }, (_, index) => new Intl.DateTimeFormat('en', { month: 'short', timeZone: 'UTC' }).format(Date.UTC(2026, index, 1)))
const LONG = Array.from({ length: 12 }, (_, index) => new Intl.DateTimeFormat('en', { month: 'long', timeZone: 'UTC' }).format(Date.UTC(2026, index, 1)))

/** One bar per month of the year; hovering or focusing a month lists its categories. A single series, so no legend. */
export function MonthlyChart({ expenses, year }: { expenses: Expense[]; year: number }) {
  const [active, setActive] = useState<number | null>(null)
  const months = MONTHS.map((_, index) => {
    // A whole-year cost is spread evenly, a twelfth in each month.
    const items = expenses.filter(expense => expense.allYear || Number(expense.spentOn.slice(5, 7)) === index + 1)
      .map(expense => expense.allYear ? { ...expense, amountUsd: expense.amountUsd / 12 } : expense)
    const byCategory = new Map<BudgetCategory, number[]>()
    for (const item of items) byCategory.set(item.category, [...(byCategory.get(item.category) ?? []), item.amountUsd])
    return {
      total: sumUsd(items.map(item => item.amountUsd)), count: items.length,
      unpaid: sumUsd(items.filter(item => !item.isPaid).map(item => item.amountUsd)),
      categories: [...byCategory].map(([category, amounts]) => ({ category, amount: sumUsd(amounts) })).sort((a, b) => b.amount - a.amount),
    }
  })
  const max = Math.max(...months.map(month => month.total))
  if (max === 0) return <p className="finance-empty">No expenses recorded in {year} yet.</p>
  const peak = months.findIndex(month => month.total === max)
  const ticks = [max, max / 2].map(value => ({ value, label: formatCompactUsd(value) }))
  const shown = active ?? peak

  return <div className="month-chart">
    <div className="month-chart-plot" onMouseLeave={() => setActive(null)}>
      <div className="month-chart-grid" aria-hidden="true">{ticks.map(tick => <span key={tick.value} style={{ bottom: `${(tick.value / max) * 100}%` }}><small>{tick.label}</small></span>)}</div>
      {months.map((month, index) => <button key={MONTHS[index]} type="button" className={`month-chart-col ${index === shown ? 'is-active' : ''}`}
        aria-label={`${LONG[index]} ${year}: ${formatCents(month.total)} in ${month.count} ${month.count === 1 ? 'expense' : 'expenses'}`}
        onMouseEnter={() => setActive(index)} onFocus={() => setActive(index)} onBlur={() => setActive(null)}>
        <span className="month-chart-bar" style={{ height: `${(month.total / max) * 100}%` }} />
        {index === peak && <span className="month-chart-peak mono" style={{ bottom: `calc(${(month.total / max) * 100}% + 4px)` }}>{formatCompactUsd(month.total)}</span>}
        <span className="month-chart-label">{MONTHS[index]}</span>
      </button>)}
    </div>
    <div className="month-chart-detail" aria-live="polite">
      <strong>{LONG[shown]} {year}</strong>
      <span className="mono">{formatCents(months[shown].total)}</span>
      <small>{months[shown].count} {months[shown].count === 1 ? 'expense' : 'expenses'}{months[shown].unpaid > 0 ? ` · ${formatCents(months[shown].unpaid)} unpaid` : ''}</small>
      {months[shown].categories.length > 0 && <ul>{months[shown].categories.map(item => <li key={item.category}>
        <span>{budgetCategoryLabels[item.category]}</span><span className="mono">{formatCents(item.amount)}</span></li>)}</ul>}
    </div>
  </div>
}
