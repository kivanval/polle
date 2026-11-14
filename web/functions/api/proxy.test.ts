import { afterEach, describe, expect, it, vi } from 'vitest'
import { onRequest } from './[[path]]'

const env = { API_ORIGIN: 'https://api.example.test', API_PROXY_SECRET: 'x'.repeat(32) }
const challengePage = '<!DOCTYPE html><html><head><title>Just a moment...</title></head><body>'
  + '<script src="/cdn-cgi/challenge-platform/h/b/orchestrate/chl_page/v1"></script></body></html>'
const html = (status: number, page: string) =>
  new Response(page, { status, headers: { 'Content-Type': 'text/html; charset=UTF-8' } })
const problem = (status: number) =>
  Response.json({ status, detail: 'From the API' }, { status, headers: { 'Content-Type': 'application/problem+json' } })
const call = (init?: RequestInit) => onRequest({ request: new Request('https://polle.example.test/api/auth/login', init), env })

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers() })

describe('API proxy', () => {
  it('forwards the browser User-Agent and sets one when it is missing', async () => {
    const fetch = vi.fn(async () => Response.json({ ok: true }))
    vi.stubGlobal('fetch', fetch)
    await call({ headers: { 'User-Agent': 'Mozilla/5.0 Test' } })
    await call()
    const agent = (index: number) => new Headers((fetch.mock.calls[index] as unknown as [URL, RequestInit])[1].headers).get('user-agent')
    expect(agent(0)).toBe('Mozilla/5.0 Test')
    expect(agent(1)).toBe('Polle-Web-Proxy/1.0')
  })

  it('retries a challenge page and sends the same body again', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(html(403, challengePage))
      .mockResolvedValueOnce(Response.json({ id: 'user-1' }))
    vi.stubGlobal('fetch', fetch)
    vi.useFakeTimers()
    const pending = call({ method: 'POST', body: '{"email":"a@b.test"}' })
    await vi.runAllTimersAsync()
    const response = await pending
    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ id: 'user-1' })
    const bodies = fetch.mock.calls.map(([, init]) => new TextDecoder().decode((init as RequestInit).body as Uint8Array))
    expect(bodies).toEqual(['{"email":"a@b.test"}', '{"email":"a@b.test"}'])
  })

  it('retries platform 503/504s and challenges silently, at most five times 3–5 s apart, then passes the answer on', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn()
      .mockResolvedValueOnce(html(503, '<html>Service Unavailable</html>'))
      .mockResolvedValueOnce(new Response('', { status: 403, headers: { 'cf-mitigated': 'challenge' } }))
      .mockResolvedValueOnce(html(504, '<html>Gateway Timeout</html>'))
      .mockResolvedValueOnce(html(502, '<html>Bad Gateway</html>'))
      .mockResolvedValueOnce(html(403, challengePage))
      .mockResolvedValueOnce(html(503, challengePage))
    vi.stubGlobal('fetch', fetch)
    const pending = call()
    await vi.advanceTimersByTimeAsync(2_999)
    expect(fetch).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(fetch).toHaveBeenCalledTimes(2)
    await vi.runAllTimersAsync()
    const response = await pending
    expect(fetch).toHaveBeenCalledTimes(6)
    expect(response.status).toBe(503)
  })

  it('stops retrying when the next pause would pass the 60-second budget', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async () => {
      await new Promise(resolve => setTimeout(resolve, 27_000))
      return html(503, '<html>Service Unavailable</html>')
    })
    vi.stubGlobal('fetch', fetch)
    const pending = call()
    await vi.runAllTimersAsync()
    // 27 s + 3 s pause + 27 s = 57 s; another 3.5 s pause would end past 60 s, so the last 503 goes out at 57 s.
    expect((await pending).status).toBe(503)
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('answers 504 when the API sends nothing within 60 seconds', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn((_: URL, init: RequestInit) => new Promise((_resolve, reject) => {
      init.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
    })))
    const pending = call()
    await vi.advanceTimersByTimeAsync(59_999)
    let settled = false
    void pending.then(() => { settled = true })
    await vi.advanceTimersByTimeAsync(0)
    expect(settled).toBe(false)
    await vi.advanceTimersByTimeAsync(1)
    const response = await pending
    expect(response.status).toBe(504)
    expect(response.headers.get('content-type')).toContain('json')
  })

  it('refuses bodies over 1 MB without calling the API, even without Content-Length', async () => {
    const fetch = vi.fn(async () => Response.json({ ok: true }))
    vi.stubGlobal('fetch', fetch)
    const chunk = new Uint8Array(256 * 1024)
    const stream = new ReadableStream<Uint8Array>({
      start(controller) { for (let i = 0; i < 5; i++) controller.enqueue(chunk); controller.close() },
    })
    const response = await call({ method: 'POST', body: stream, duplex: 'half' } as RequestInit)
    expect(response.status).toBe(413)
    expect((await call({ method: 'POST', body: 'x'.repeat(2 * 1024 * 1024) })).status).toBe(413)
    expect(fetch).not.toHaveBeenCalled()
  })

  it('answers JSON 503 instead of the platform’s HTML once the retries are spent', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn(async () => html(502, '<html>Bad Gateway</html>')))
    const pending = call()
    await vi.runAllTimersAsync()
    const response = await pending
    expect(response.status).toBe(503)
    expect(response.headers.get('content-type')).toContain('json')
    expect(await response.json()).toMatchObject({ title: 'Server is waking up', detail: 'Server is waking up, please wait a few seconds and try again.' })
  })

  it('turns other HTML server errors into the same JSON 503, but passes a challenge page on', async () => {
    for (const status of [500, 520, 524]) {
      vi.stubGlobal('fetch', vi.fn(async () => html(status, '<html>Origin error</html>')))
      const response = await call({ method: 'POST', body: '{}' })
      expect(response.status).toBe(503)
      expect((await response.json()).title).toBe('Server is waking up')
    }
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn(async () => html(403, challengePage)))
    const pending = call()
    await vi.runAllTimersAsync()
    const response = await pending
    expect(response.status).toBe(403)
    expect(await response.text()).toContain('Just a moment...')
  })

  it('retries Cloudflare’s cannot-connect errors (521/522/523/530) while the host wakes', async () => {
    vi.useFakeTimers()
    const fetch = vi.fn()
    for (const status of [521, 522, 523, 530]) fetch.mockResolvedValueOnce(html(status, '<html>error</html>'))
    fetch.mockResolvedValueOnce(Response.json({ id: 'user-1' }))
    vi.stubGlobal('fetch', fetch)
    const pending = call({ method: 'POST', body: '{}' })
    await vi.runAllTimersAsync()
    expect((await pending).status).toBe(200)
    expect(fetch).toHaveBeenCalledTimes(5)
  })

  it('retries a dropped connection for reads and sign-in, but not for other writes', async () => {
    vi.useFakeTimers()
    const dropped = () => vi.fn()
      .mockRejectedValueOnce(new TypeError('Network connection lost.'))
      .mockResolvedValueOnce(Response.json({ ok: true }))
    for (const [path, method] of [['/api/auth/me', 'GET'], ['/api/auth/csrf', 'GET'], ['/api/auth/login', 'POST']] as const) {
      const fetch = dropped()
      vi.stubGlobal('fetch', fetch)
      const pending = onRequest({ request: new Request(`https://polle.example.test${path}`, { method, body: method === 'POST' ? '{}' : undefined }), env })
      await vi.runAllTimersAsync()
      expect((await pending).status).toBe(200)
      expect(fetch).toHaveBeenCalledTimes(2)
    }
    const fetch = dropped()
    vi.stubGlobal('fetch', fetch)
    const response = await onRequest({ request: new Request('https://polle.example.test/api/farms', { method: 'POST', body: '{}' }), env })
    expect(response.status).toBe(503)
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('does not retry the API’s own 429 and 503', async () => {
    for (const status of [429, 503]) {
      const fetch = vi.fn(async () => problem(status))
      vi.stubGlobal('fetch', fetch)
      const response = await call({ method: 'POST', body: '{}' })
      expect(fetch).toHaveBeenCalledTimes(1)
      expect(response.status).toBe(status)
    }
  })
})
