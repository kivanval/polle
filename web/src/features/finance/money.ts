// Pure money and payroll helpers for the resources page. Sums run in whole cents so totals match the ledger to the cent.

import type { Worker, WorkerShift } from './api'

const cents = (value: number) => Math.round(value * 100)
/** Adds USD amounts in whole cents, so 0.1 + 0.2 is exactly 0.30. */
export const sumUsd = (values: number[]) => values.reduce((sum, value) => sum + cents(value), 0) / 100

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
/** Always shows cents: $1,204.50, −$12.00. */
export const formatCents = (value: number) => usd.format(value)
const compactUsd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', notation: 'compact', maximumFractionDigits: 1 })
export const formatCompactUsd = (value: number) => compactUsd.format(value)

const quantity = new Intl.NumberFormat('en-US', { maximumFractionDigits: 3 })
export const formatQuantity = (value: number) => quantity.format(value)
const price = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 4 })
export const formatUnitPrice = (value: number) => price.format(value)

/** Quantity × unit price rounded half away from zero to cents, like the API. */
export const lineAmount = (qty: number, unitPrice: number) => {
  const raw = qty * unitPrice
  // Float products such as 1.005 land a hair under the half; nudge by a tiny epsilon before rounding.
  return Math.sign(raw) * Math.round(Math.abs(raw) * 100 + 1e-7) / 100
}

const minutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))
/** Length of a same-day shift in hours; 23:59 stands for the end of the day. */
export const shiftHours = (shift: Pick<WorkerShift, 'startTime' | 'endTime'>) => {
  const end = shift.endTime.startsWith('23:59') ? 24 * 60 : minutes(shift.endTime)
  return Math.max(0, end - minutes(shift.startTime)) / 60
}

/** Hours of the worker's week in a month, by ISO weekday (index 0 = Monday). */
export function weekdayHours(shifts: WorkerShift[], month: number) {
  const days = Array<number>(7).fill(0)
  for (const shift of shifts) if (shift.month === month) days[shift.dayOfWeek - 1] += shiftHours(shift)
  return days
}
export const weekHours = (shifts: WorkerShift[], month: number) => weekdayHours(shifts, month).reduce((sum, hours) => sum + hours, 0)

/** Scheduled hours in a calendar month: each date counts the hours of its weekday in that month's week. */
export function monthHours(shifts: WorkerShift[], year: number, month: number) {
  const byDay = weekdayHours(shifts, month)
  const length = new Date(Date.UTC(year, month, 0)).getUTCDate()
  let total = 0
  for (let date = 1; date <= length; date++) total += byDay[(new Date(Date.UTC(year, month - 1, date)).getUTCDay() + 6) % 7]
  return total
}

/**
 * Planned pay for a calendar month: a salary in full, or scheduled hours × the hourly rate. Nothing for an inactive
 * worker or a month that ends before their hiring date. A salary is not prorated for a mid-month start.
 */
export function plannedPay(worker: Worker, year: number, month: number) {
  if (!worker.isActive) return 0
  const lastDay = `${year}-${String(month).padStart(2, '0')}-${String(new Date(Date.UTC(year, month, 0)).getUTCDate()).padStart(2, '0')}`
  if (worker.hiredOn && worker.hiredOn > lastDay) return 0
  if (worker.payType === 'monthly') return worker.payRateUsd
  return cents(monthHours(worker.shifts, year, month) * worker.payRateUsd) / 100
}

export const plannedPayYear = (worker: Worker, year: number) =>
  sumUsd(Array.from({ length: 12 }, (_, index) => plannedPay(worker, year, index + 1)))

/** RFC 4180 CSV with a BOM so spreadsheet apps read UTF-8 names correctly. */
export function toCsv(rows: (string | number | null)[][]) {
  const cell = (value: string | number | null) => {
    const text = value === null ? '' : String(value)
    return /[",\n\r;]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
  }
  return '﻿' + rows.map(row => row.map(cell).join(',')).join('\r\n')
}

export function download(name: string, content: string, type = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([content], { type }))
  const link = Object.assign(document.createElement('a'), { href: url, download: name })
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

export const payLabel = (worker: Worker) => `${formatCents(worker.payRateUsd)} ${worker.payType === 'hourly' ? '/ h' : '/ month'}`
