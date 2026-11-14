import { dateMatchModifiers, type Matcher } from 'react-day-picker'

/** The days of a month crops are usually sown on, and the ones the planner (felder) plans sowings for. */
export const SOWING_DAYS = [1, 11, 21] as const

const pad = (value: number) => String(value).padStart(2, '0')
/** A local calendar day as YYYY-MM-DD. */
export const toIso = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
const ordinal = (day: number) => `${day}${day % 10 === 1 && day !== 11 ? 'st' : day % 10 === 2 && day !== 12 ? 'nd' : day % 10 === 3 && day !== 13 ? 'rd' : 'th'}`

/** The usual sowing days of a month as ISO dates with their labels; a day another crop holds, or past the harvest, is off. */
export function sowingDayChoices(month: Date, disabledDays: Matcher[]) {
  return SOWING_DAYS.map(day => {
    const date = new Date(month.getFullYear(), month.getMonth(), day)
    return { iso: toIso(date), label: ordinal(day), disabled: dateMatchModifiers(date, disabledDays) }
  })
}
