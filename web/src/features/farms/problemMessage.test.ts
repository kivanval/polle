import { describe, expect, it } from 'vitest'
import { ApiError } from '@/shared/api/client'
import { problemMessage, SERVER_UNAVAILABLE, SOLVER_UNAVAILABLE } from './api'

describe('problemMessage', () => {
  it('names the strategy solver when it is what is unavailable', () => {
    const error = new ApiError(503, { status: 503, title: 'Strategy solver unavailable', detail: 'The strategy solver is temporarily unavailable.', code: 'solver_unavailable' })

    expect(problemMessage(error)).toBe(SOLVER_UNAVAILABLE)
  })

  it('says our server is unavailable when its database is down, never that the workspace is', () => {
    const error = new ApiError(503, { status: 503, title: 'Database temporarily unavailable', code: 'database_unavailable' })

    expect(problemMessage(error)).toBe(SERVER_UNAVAILABLE)
    expect(problemMessage(error)).not.toMatch(/workspace/i)
  })

  it('keeps the waking-up message of the host and its proxy', () => {
    const error = new ApiError(503, { title: 'Server is waking up', status: 503, detail: 'Server is waking up, please wait a few seconds and try again.' })

    expect(problemMessage(error)).toBe('Server is waking up, please wait a few seconds and try again.')
  })

  it('shows the API’s own explanation of a server fault', () => {
    const error = new ApiError(500, { status: 500, detail: 'Something went wrong on our server. Please try again shortly.', code: 'server_error' })

    expect(problemMessage(error)).toBe('Something went wrong on our server. Please try again shortly.')
  })

  it('says the connection failed when no answer came back', () => {
    expect(problemMessage(new TypeError('Failed to fetch'))).toMatch(/couldn’t reach Polle/)
  })
})
