import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { MonthsMismatch } from './MonthsMismatch'

describe('different months warning', () => {
  it('is a warning that names both periods and what to do', () => {
    const html = renderToStaticMarkup(<MonthsMismatch plan={{ startMonth: '2026-12', endMonth: '2031-12' }} strategies={{ startMonth: '2027-01', endMonth: '2032-12' }} />)
    expect(html).toContain('class="strategy-mismatch"')
    expect(html).toContain('strategy-mismatch-icon')
    expect(html).toContain('Not compared')
    expect(html.match(/<b>[^<]+<\/b>/g)).toHaveLength(2)
    expect(html).toContain('Evaluate your plan for the same months to compare.')
    expect(html).toContain('Suggested plans: ')
    expect(html).not.toMatch(/>[^<]*strateg/i)
  })

  it('says only that the plan isn’t evaluated yet when it has no evaluation for its current crops', () => {
    const html = renderToStaticMarkup(<MonthsMismatch notEvaluated />)
    expect(html).toContain('class="strategy-mismatch"')
    expect(html).toContain('Not compared yet: your current plan hasn’t been evaluated.')
    expect(html).toContain('<b>Evaluate my plan</b>')
    expect(html).not.toContain('months')
  })
})
