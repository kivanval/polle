import { describe, expect, it } from 'vitest'
import { isDomDesyncError } from './domDesync'

describe('isDomDesyncError', () => {
  it('recognises the DOMException React surfaces after a translator rewrote text nodes', () => {
    const error = new Error("Failed to execute 'insertBefore' on 'Node': The node before which the new node is to be inserted is not a child of this node.")
    error.name = 'NotFoundError'
    expect(isDomDesyncError(error)).toBe(true)
  })

  it('recognises the removeChild message even without the NotFoundError name', () => {
    expect(isDomDesyncError(new Error("Failed to execute 'removeChild' on 'Node': The node to be removed is not a child of this node."))).toBe(true)
  })

  it('leaves ordinary render errors alone', () => {
    expect(isDomDesyncError(new TypeError("Cannot read properties of undefined (reading 'cycles')"))).toBe(false)
    expect(isDomDesyncError('insertBefore')).toBe(false)
  })
})
