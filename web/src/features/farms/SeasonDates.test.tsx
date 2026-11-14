import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { SeasonDates, VerticalCalendar } from './SeasonDates'

const months = (html: string) => (html.match(/class="rdp-month"/g) ?? []).length

describe('vertical sowing and harvest calendar', () => {
  it('stacks many months in one scrolling strip with no month arrows', () => {
    const html = renderToStaticMarkup(<SeasonDates id="a" sown="2026-04-11" harvested="" occupied={[]} maxHarvest={null} disabled={false} focusHarvest
      onChange={() => {}} />)
    expect(html).toContain('crop-calendar is-vertical')
    expect(html).toContain('crop-calendar-scroll')
    expect(months(html)).toBeGreaterThan(6)
    expect(html).not.toContain('rdp-button_previous')
    expect(html).not.toContain('rdp-button_next')
  })

  it('pins one weekday row from Monday above the months', () => {
    const html = renderToStaticMarkup(<VerticalCalendar label="Sowing date" selected={undefined} anchor={new Date(2026, 9, 1)}
      first={new Date(1950, 0, 1)} last={new Date(2056, 11, 31)} disabledDays={[]} taken={[]} sowingValue="" onSelect={() => {}} onClose={() => {}} />)
    const weekdays = html.match(/<div class="crop-calendar-weekdays"[^>]*>(.*?)<\/div>/)?.[1] ?? ''
    expect(weekdays.replace(/<[^>]+>/g, ' ').trim().split(/\s+/)).toEqual(['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'])
    // DayPicker's own per-month weekday rows are hidden, so only the pinned row shows.
    expect(html).not.toContain('rdp-weekdays')
  })

  it('keeps the 1st, 11th and 21st one click away for sowing, naming the month in view', () => {
    const html = renderToStaticMarkup(<VerticalCalendar label="Sowing date" selected={undefined} anchor={new Date(2026, 9, 1)}
      first={new Date(1950, 0, 1)} last={new Date(2056, 11, 31)} disabledDays={[]} taken={[]} sowingValue="" onSelect={() => {}} onClose={() => {}} />)
    expect(html).toContain('Sow on the')
    for (const label of ['1st', '11th', '21st']) expect(html).toContain(`>${label}</button>`)
    expect(html).toContain('of Oct 2026')
  })

  it('offers earlier and later months around the window', () => {
    const html = renderToStaticMarkup(<VerticalCalendar label="Harvest date" selected={new Date(2026, 6, 15)} anchor={new Date(2026, 6, 1)}
      first={new Date(1950, 0, 1)} last={new Date(2056, 11, 31)} disabledDays={[]} taken={[]} sowingValue={null} onSelect={() => {}} onClose={() => {}} />)
    expect(html).toContain('Earlier months')
    expect(html).toContain('Later months')
    expect(html).not.toContain('Sow on the')
  })
})
