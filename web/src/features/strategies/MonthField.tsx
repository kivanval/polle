import { useState } from 'react'
import { MONTHS, joinMonth, splitMonth, yearOptions } from './month'

/** A month as two selects, labelled as one field. Value and onChange use YYYY-MM. */
export function MonthField({ id, label, value, onChange }: { id: string; label: string; value: string; onChange: (value: string) => void }) {
  const [thisYear] = useState(() => new Date().getUTCFullYear())
  const { year, month } = splitMonth(value)
  const years = yearOptions(thisYear)
  return <fieldset className="month-field">
    <legend>{label}</legend>
    <label className="visually-hidden" htmlFor={`${id}-month`}>{label} month</label>
    <select id={`${id}-month`} value={month} onChange={event => onChange(joinMonth(year, Number(event.target.value)))}>
      {MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}
    </select>
    <label className="visually-hidden" htmlFor={`${id}-year`}>{label} year</label>
    <select id={`${id}-year`} value={year} onChange={event => onChange(joinMonth(Number(event.target.value), month))}>
      {(years.includes(year) ? years : [year, ...years]).map(option => <option key={option} value={option}>{option}</option>)}
    </select>
  </fieldset>
}
