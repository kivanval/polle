import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { PlanningActions } from './PlanningActions'

const render = (overrides: Partial<Parameters<typeof PlanningActions>[0]> = {}) => renderToStaticMarkup(
  <PlanningActions disabled={false} suggesting={false} evaluating={false} customizing={false} choiceLabel="3 chosen plans"
    onEvaluate={() => {}} onCustomize={() => {}} {...overrides} />)

describe('PlanningActions', () => {
  it('puts both launch actions together, Evaluate my plan first, and the plan settings only after them', () => {
    const html = render()
    const evaluate = html.indexOf('Evaluate my plan')
    const suggest = html.indexOf('Suggest plans')
    const settings = html.indexOf('Choose which plans to suggest')
    const count = html.indexOf('3 chosen plans')

    expect(evaluate).toBeGreaterThanOrEqual(0)
    expect(evaluate).toBeLessThan(suggest)
    expect(suggest).toBeLessThan(settings)
    expect(settings).toBeLessThan(count)
  })

  it('keeps Suggest plans as the submit button and Evaluate my plan as a plain button', () => {
    const html = render()
    expect(html).toMatch(/<button type="submit"[^>]*>(?:(?!<\/button>).)*Suggest plans/)
    expect(html).toMatch(/<button type="button"[^>]*>(?:(?!<\/button>).)*Evaluate my plan/)
  })

  it('says when it is evaluating, disables both actions together, and hides the count until the presets load', () => {
    const html = render({ evaluating: true, disabled: true, describedBy: 'strategy-no-soil', choiceLabel: null })
    expect(html).toContain('Evaluating…')
    expect(html.match(/disabled=""/g)).toHaveLength(2)
    expect(html.match(/aria-describedby="strategy-no-soil"/g)).toHaveLength(2)
    expect(html).not.toContain('strategy-choice-count')
  })
})
