// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SeasonDates } from './SeasonDates'

declare global { var IS_REACT_ACT_ENVIRONMENT: boolean }
globalThis.IS_REACT_ACT_ENVIRONMENT = true

let host: HTMLDivElement, root: Root

beforeEach(() => {
  vi.useFakeTimers()
  // jsdom lays nothing out, so the strip's scroll to the chosen month has nothing to do.
  Element.prototype.scrollTo = () => {}
  host = document.createElement('div')
  document.body.append(host)
  root = createRoot(host)
  act(() => root.render(<SeasonDates id="a" sown="2026-04-21" harvested="" occupied={[]} maxHarvest={null} disabled={false} onChange={() => {}} />))
})

afterEach(() => {
  act(() => root.unmount())
  host.remove()
  vi.useRealTimers()
})

const sownField = () => host.querySelector<HTMLButtonElement>('#sown-a')!
const calendar = () => host.querySelector('#calendar-a')
const click = (element: Element) => act(() => { element.dispatchEvent(new MouseEvent('click', { bubbles: true })) })
const press = (element: Element, key: string) => act(() => { element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true })) })
const mouseDown = (element: Element) => act(() => { element.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })) })
/** Lets the fold-away animation finish. */
const settle = () => act(() => { vi.runAllTimers() })

function openSown() {
  click(sownField())
  expect(calendar()).not.toBeNull()
  expect(sownField().getAttribute('aria-expanded')).toBe('true')
}

function expectFoldedAway() {
  // The field reads as collapsed at once while the calendar folds, and the calendar leaves once it has.
  expect(sownField().getAttribute('aria-expanded')).toBe('false')
  expect(calendar()?.classList.contains('is-closing')).toBe(true)
  settle()
  expect(calendar()).toBeNull()
}

describe('opening and folding the sowing calendar', () => {
  it('opens from the date field', () => {
    expect(calendar()).toBeNull()
    openSown()
    expect(calendar()!.classList.contains('is-closing')).toBe(false)
  })

  it('folds away from Done in its header', () => {
    openSown()
    const done = host.querySelector('.crop-calendar-bar .crop-calendar-collapse')!
    expect(done.textContent).toContain('Done')
    click(done)
    expectFoldedAway()
    expect(document.activeElement).toBe(sownField())
  })

  it('has no second Done at the bottom', () => {
    openSown()
    const doneButtons = [...host.querySelectorAll('button')].filter(button => button.textContent?.trim() === 'Done')
    expect(doneButtons).toHaveLength(1)
  })

  it('folds away on a second press of the open date field', () => {
    openSown()
    click(sownField())
    expectFoldedAway()
  })

  it('folds away on Esc inside the calendar', () => {
    openSown()
    press(host.querySelector('.crop-calendar')!, 'Escape')
    expectFoldedAway()
  })

  it('folds away on Esc on the date field', () => {
    openSown()
    press(sownField(), 'Escape')
    expectFoldedAway()
  })

  it('folds away on a press outside, but not on a press inside', () => {
    openSown()
    mouseDown(host.querySelector('.crop-calendar-scroll')!)
    expect(calendar()!.classList.contains('is-closing')).toBe(false)
    mouseDown(document.body)
    expectFoldedAway()
  })

  it('opens again straight away when the field is pressed while folding', () => {
    openSown()
    click(sownField())
    click(sownField())
    settle()
    expect(calendar()).not.toBeNull()
    expect(calendar()!.classList.contains('is-closing')).toBe(false)
  })
})
