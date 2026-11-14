import { describe, expect, it } from 'vitest'
import { SOWING_DAYS, sowingDayChoices } from './sowingDays'

describe('usual sowing days', () => {
  it('offers the 1st, 11th and 21st, the days the planner sows on', () => {
    expect(SOWING_DAYS).toEqual([1, 11, 21])
    expect(sowingDayChoices(new Date(2027, 4, 1), [])).toEqual([
      { iso: '2027-05-01', label: '1st', disabled: false },
      { iso: '2027-05-11', label: '11th', disabled: false },
      { iso: '2027-05-21', label: '21st', disabled: false },
    ])
  })

  it('takes the month shown, whatever day the calendar is on', () => {
    expect(sowingDayChoices(new Date(2028, 8, 17), []).map(day => day.iso)).toEqual(['2028-09-01', '2028-09-11', '2028-09-21'])
  })

  it('turns off a day another crop holds or one after the harvest', () => {
    const choices = sowingDayChoices(new Date(2027, 3, 1), [{ from: new Date(2027, 2, 1), to: new Date(2027, 3, 5) }, { after: new Date(2027, 3, 15) }])
    expect(choices.map(day => day.disabled)).toEqual([true, false, true])
  })
})
