import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, ServerChallengeError, apiFetch, challenge, isChallengePage, isWakingUp, onSessionExpired, wakeApiHost } from './client'

const challengePage = '<!DOCTYPE html><html><head><title>Just a moment...</title></head>'
  + '<body><script src="/cdn-cgi/challenge-platform/h/b/orchestrate/chl_page/v1?ray=1"></script>'
  + '<form action="/?__cf_chl_f_tk=abc"></form></body></html>'
const html = (status: number, page: string, extra: Record<string, string> = {}) =>
  new Response(page, { status, headers: { 'Content-Type': 'text/html; charset=UTF-8', ...extra } })

const reply = (status: number, body?: unknown) => vi.fn(async () => body === undefined
  ? new Response(null, { status })
  : Response.json(body, { status }))

afterEach(() => vi.unstubAllGlobals())

describe('api client', () => {
  it('explains an expired session instead of a generic failure', async () => {
    vi.stubGlobal('fetch', reply(401))
    const error = await apiFetch('/farms').catch((reason: unknown) => reason)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).message).toBe('Your session has expired. Please log in again.')
  })

  it('keeps the API’s own explanation', async () => {
    vi.stubGlobal('fetch', reply(401, { detail: 'The email or password is incorrect.' }))
    await expect(apiFetch('/auth/me')).rejects.toThrow('The email or password is incorrect.')
  })

  it('reports an expired session for regular requests', async () => {
    const listener = vi.fn()
    const stop = onSessionExpired(listener)
    vi.stubGlobal('fetch', reply(401))
    await apiFetch('/farms').catch(() => undefined)
    stop()
    await apiFetch('/farms').catch(() => undefined)
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('does not treat sign-in failures or the session check as an expired session', async () => {
    const listener = vi.fn()
    const stop = onSessionExpired(listener)
    vi.stubGlobal('fetch', reply(401, { detail: 'The email or password is incorrect.' }))
    await apiFetch('/auth/me').catch(() => undefined)
    stop()
    expect(listener).not.toHaveBeenCalled()
  })

  it('does not report other failures', async () => {
    const listener = vi.fn()
    const stop = onSessionExpired(listener)
    vi.stubGlobal('fetch', reply(500))
    await expect(apiFetch('/farms')).rejects.toThrow('We couldn’t complete this request. Please try again.')
    stop()
    expect(listener).not.toHaveBeenCalled()
  })
})

describe('waiting for a sleeping server', () => {
  afterEach(() => vi.useRealTimers())
  const waking = { title: 'Server is waking up', status: 503, detail: 'Server is waking up, please wait a few seconds and try again.' }

  it('retries the session check twice on 502/503/504, 2 s then 5 s apart', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn()
      .mockResolvedValueOnce(Response.json(waking, { status: 503 }))
      .mockResolvedValueOnce(html(504, '<html>Gateway Timeout</html>'))
      .mockResolvedValueOnce(Response.json({ id: 'user-1' }))
    vi.stubGlobal('fetch', fetch)
    const result = apiFetch('/auth/me')
    await vi.advanceTimersByTimeAsync(1_999)
    expect(fetch).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(fetch).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(5_000)
    await expect(result).resolves.toEqual({ id: 'user-1' })
    expect(fetch).toHaveBeenCalledTimes(3)
  })

  it('retries a dropped connection on sign-in and its token request', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(Response.json({ token: 't' }))
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(Response.json({ id: 'user-1' }))
    vi.stubGlobal('fetch', fetch)
    const result = apiFetch('/auth/login', { method: 'POST', body: '{}' })
    await vi.runAllTimersAsync()
    await expect(result).resolves.toEqual({ id: 'user-1' })
    expect(fetch.mock.calls.map(([url]) => String(url).replace(/^.*\/api/, ''))).toEqual(['/auth/csrf', '/auth/csrf', '/auth/login', '/auth/login'])
  })

  it('gives up after two retries with the proxy’s explanation, and says so for bare platform errors too', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async () => Response.json(waking, { status: 503 }))
    vi.stubGlobal('fetch', fetch)
    const result = apiFetch('/auth/me').catch((reason: unknown) => reason)
    await vi.runAllTimersAsync()
    const error = await result
    expect(fetch).toHaveBeenCalledTimes(3)
    expect(isWakingUp(error)).toBe(true)
    expect((error as ApiError).message).toBe(waking.detail)
    vi.stubGlobal('fetch', reply(502))
    const bare = await apiFetch('/farms').catch((reason: unknown) => reason)
    expect(isWakingUp(bare)).toBe(true)
    expect((bare as ApiError).message).toBe('The server is waking up. Please wait a few seconds and try again.')
  })

  it('does not retry other requests or the API’s own errors', async () => {
    const fetch = reply(502)
    vi.stubGlobal('fetch', fetch)
    await apiFetch('/farms').catch(() => undefined)
    expect(fetch).toHaveBeenCalledTimes(1)
    const own = reply(401, { detail: 'The email or password is incorrect.' })
    vi.stubGlobal('fetch', own)
    await apiFetch('/auth/me').catch(() => undefined)
    expect(own).toHaveBeenCalledTimes(1)
    expect(isWakingUp(new ApiError(503, { status: 503, detail: 'Database unavailable' }))).toBe(false)
  })

  it('stops waiting when the caller aborts', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn(async () => html(503, '<html>Service Unavailable</html>')))
    const controller = new AbortController()
    const result = apiFetch('/auth/me', { signal: controller.signal }).catch((reason: unknown) => reason)
    await vi.advanceTimersByTimeAsync(0)
    controller.abort()
    expect(await result).toBeInstanceOf(DOMException)
  })
})

describe('server challenge', () => {
  afterEach(() => challenge.cancel())

  it('recognizes the Cloudflare challenge page but not other HTML errors', () => {
    expect(isChallengePage(html(403, challengePage).headers, challengePage)).toBe(true)
    expect(isChallengePage(new Headers({ 'cf-mitigated': 'challenge' }), '')).toBe(true)
    expect(isChallengePage(html(502, '<html><title>Bad gateway</title></html>').headers, '<html><title>Bad gateway</title></html>')).toBe(false)
    expect(isChallengePage(new Headers({ 'Content-Type': 'application/json' }), { detail: 'Just a moment...' })).toBe(false)
  })

  it('holds a challenged request and finishes it on retry', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(html(403, challengePage))
      .mockResolvedValueOnce(Response.json([{ id: 'farm-1' }]))
    vi.stubGlobal('fetch', fetch)
    const result = apiFetch<{ id: string }[]>('/farms')
    await vi.waitFor(() => expect(challenge.count()).toBe(1))
    challenge.retry()
    await expect(result).resolves.toEqual([{ id: 'farm-1' }])
    expect(challenge.count()).toBe(0)
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('keeps waiting while the challenge is still there', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => html(403, challengePage)))
    const result = apiFetch('/farms').catch((reason: unknown) => reason)
    await vi.waitFor(() => expect(challenge.count()).toBe(1))
    challenge.retry()
    await vi.waitFor(() => expect(challenge.count()).toBe(1))
    challenge.cancel()
    expect(await result).toBeInstanceOf(ServerChallengeError)
    expect(challenge.count()).toBe(0)
  })

  it('does not hold sign-in requests behind the banner; the caller gets the challenge error', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => html(403, challengePage)))
    const result = apiFetch('/auth/me').catch((reason: unknown) => reason)
    expect(await result).toBeInstanceOf(ServerChallengeError)
    expect(challenge.count()).toBe(0)
  })

  it('lets the caller abort a held request', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => html(403, challengePage)))
    const controller = new AbortController()
    const result = apiFetch('/farms', { signal: controller.signal }).catch((reason: unknown) => reason)
    await vi.waitFor(() => expect(challenge.count()).toBe(1))
    controller.abort()
    expect(await result).toBeInstanceOf(DOMException)
    expect(challenge.count()).toBe(0)
  })
})

describe('waking the API host directly', () => {
  it('pings the liveness route without cors or cache, at most once per 30 s', () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async () => new Response(null, { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    const url = 'https://api.example/api/health/live'
    wakeApiHost(url)
    wakeApiHost(url)
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch).toHaveBeenCalledWith(url, expect.objectContaining({ mode: 'no-cors', cache: 'no-store', credentials: 'omit' }))
    vi.advanceTimersByTime(30_000)
    wakeApiHost(url)
    expect(fetch).toHaveBeenCalledTimes(2)
    vi.useRealTimers()
  })

  it('stays quiet without a wake URL', () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    wakeApiHost(null)
    expect(fetch).not.toHaveBeenCalled()
  })

  it('swallows a failed ping', async () => {
    vi.useFakeTimers()
    // Past the previous test's ping, so this one is sent.
    vi.advanceTimersByTime(120_000)
    const fetch = vi.fn(async () => { throw new TypeError('Failed to fetch') })
    vi.stubGlobal('fetch', fetch)
    expect(() => wakeApiHost('https://api.example/api/health/live')).not.toThrow()
    await vi.runAllTimersAsync()
    expect(fetch).toHaveBeenCalledTimes(1)
    vi.useRealTimers()
  })
})
