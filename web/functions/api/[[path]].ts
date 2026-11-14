// Same-origin proxy for /api/* on Cloudflare Pages. The session and antiforgery cookies are
// SameSite and the client sends credentials: 'same-origin', so the browser never calls the API origin directly.
// API_ORIGIN and API_PROXY_SECRET are set only for the Production environment, so preview deployments
// never reach production data.
interface Env { API_ORIGIN?: string; API_PROXY_SECRET?: string }

// Hop-by-hop headers (RFC 9110 §7.6.1) describe one connection and must not be forwarded.
const hopByHop = ['connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization', 'proxy-connection',
  'te', 'trailer', 'transfer-encoding', 'upgrade', 'host']

function forwardable(source: Headers): Headers {
  const headers = new Headers(source)
  for (const name of (source.get('connection') ?? '').split(',')) if (name.trim()) headers.delete(name.trim())
  for (const name of hopByHop) headers.delete(name)
  return headers
}

// Client-supplied proxy and forwarding headers are dropped; the API trusts only the values set here.
function upstreamHeaders(request: Request, secret: string): Headers {
  const headers = forwardable(request.headers)
  for (const name of [...headers.keys()])
    if (name.startsWith('x-ecocrop-') || name.startsWith('x-forwarded-') || name.startsWith('cf-') || name === 'forwarded' || name === 'x-real-ip')
      headers.delete(name)
  headers.set('X-EcoCrop-Proxy-Secret', secret)
  // The browser's User-Agent passes through. Without one, Cloudflare in front of Render scores the request as a bot,
  // as it did for the API's own solver calls (see Program.cs).
  if (!headers.get('user-agent')) headers.set('User-Agent', 'Polle-Web-Proxy/1.0')
  const client = request.headers.get('cf-connecting-ip')
  if (client) headers.set('X-EcoCrop-Client-IP', client)
  return headers
}

function unavailable(): Response {
  return Response.json(
    { title: 'Service unavailable', status: 503, detail: 'The Polle API is unavailable. Please try again shortly.' },
    { status: 503, headers: { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store' } },
  )
}

/** Far above the API's own limits (16 KB, 128 KB for prices), so only oversized or abusive bodies stop here. */
const maxBodyBytes = 1024 * 1024

function tooLarge(): Response {
  return Response.json(
    { title: 'Request too large', status: 413, detail: 'The request is too large.' },
    { status: 413, headers: { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store' } },
  )
}

/** Reads the body with a running count, so a missing or false Content-Length can't make the proxy buffer more. */
async function readBody(request: Request): Promise<Uint8Array<ArrayBuffer> | null> {
  if (Number(request.headers.get('content-length') ?? 0) > maxBodyBytes) return null
  if (!request.body) return new Uint8Array()
  const reader = request.body.getReader()
  const chunks: Uint8Array[] = []
  let size = 0
  for (;;) {
    const { done, value } = await reader.read()
    if (done) break
    size += value.byteLength
    if (size > maxBodyBytes) { await reader.cancel(); return null }
    chunks.push(value)
  }
  const body = new Uint8Array(size)
  let offset = 0
  for (const chunk of chunks) { body.set(chunk, offset); offset += chunk.byteLength }
  return body
}

/**
 * A free-tier Render host takes up to about a minute to wake from idle. The proxy waits that long for the API's own
 * answer, retrying held-back requests silently, before it passes a platform error on to the browser.
 */
const coldStartBudgetMs = 60_000
/** Pauses before each silent retry, 3–5 s apart; at most five retries, and none past the budget. */
const retryDelaysMs = [3_000, 3_500, 4_000, 4_500, 5_000]
const challengeMarkers = ['<title>Just a moment...</title>', 'challenge-platform', '__cf_chl', 'Just a moment...']

function gatewayTimeout(): Response {
  return Response.json(
    { title: 'Gateway timeout', status: 504, detail: 'The Polle API did not answer in time. Please try again shortly.' },
    { status: 504, headers: { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store' } },
  )
}

/** Fetches with a deadline on the response headers only, so a long body that has started is never cut off. */
async function fetchBefore(deadline: number, target: URL, init: RequestInit): Promise<Response | 'timeout'> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), Math.max(0, deadline - Date.now()))
  try { return await fetch(target, { ...init, signal: controller.signal }) }
  catch (error) { if (controller.signal.aborted) return 'timeout'; throw error }
  finally { clearTimeout(timer) }
}

/**
 * Platform answers that mean the request never reached the API: Render's cold-start/deploy 429/502/503/504, and
 * Cloudflare's 521/522/523/530 (could not connect to the host). 520 and 524 are left out: there the host may already
 * have the request.
 */
const notDeliveredStatuses = [429, 502, 503, 504, 521, 522, 523, 530]

/**
 * True when Cloudflare or Render answered instead of the API: a challenge (`cf-mitigated`, or the challenge page), or
 * one of the statuses above without the API's JSON. Those requests never reached the API, so sending them again is
 * safe for writes too. The API's own 429 (sign-in rate limit) and 503 (database down) are JSON and pass straight through.
 */
async function heldBackByPlatform(response: Response) {
  if (isChallenge(response.headers.get('cf-mitigated'), null)) return true
  if (response.headers.get('content-type')?.includes('json')) return false
  if (notDeliveredStatuses.includes(response.status)) return true
  return isChallenge(null, await htmlOf(response))
}

async function htmlOf(response: Response) {
  return response.headers.get('content-type')?.includes('text/html') ? response.clone().text() : null
}

function isChallenge(mitigated: string | null, page: string | null) {
  return mitigated?.toLowerCase() === 'challenge' || (page !== null && challengeMarkers.some(marker => page.includes(marker)))
}

/**
 * The browser gets JSON for every failure, so the page can say the server is starting instead of a generic error.
 * Challenge pages are the exception: the client recognises them and offers its own retry banner.
 */
async function platformErrorInsteadOfApi(response: Response) {
  if (response.headers.get('content-type')?.includes('json')) return false
  if (response.status < 500 && response.status !== 429) return false
  return !isChallenge(response.headers.get('cf-mitigated'), await htmlOf(response))
}

function wakingUp(): Response {
  return Response.json(
    { title: 'Server is waking up', status: 503, detail: 'Server is waking up, please wait a few seconds and try again.' },
    { status: 503, headers: { 'Content-Type': 'application/problem+json', 'Cache-Control': 'no-store', 'Retry-After': '5' } },
  )
}

/**
 * A dropped connection may come after the API received the request, so only requests that are safe to repeat are sent
 * again: reads, and sign-in (a repeated sign-in only issues a fresh session).
 */
function repeatableAfterNetworkError(method: string, pathname: string) {
  return ['GET', 'HEAD', 'OPTIONS'].includes(method) || (method === 'POST' && pathname === '/api/auth/login')
}

export async function onRequest({ request, env }: { request: Request; env: Env }): Promise<Response> {
  let origin: URL
  try { origin = new URL(env.API_ORIGIN ?? '') } catch { return unavailable() }
  if (origin.protocol !== 'https:' || !env.API_PROXY_SECRET) return unavailable()

  const url = new URL(request.url)
  const target = new URL(url.pathname + url.search, origin)
  const hasBody = !['GET', 'HEAD'].includes(request.method)
  const headers = upstreamHeaders(request, env.API_PROXY_SECRET)
  // A stream can be sent once; the API's bodies are small JSON, so buffering lets a held-back request go out again.
  let body: Uint8Array<ArrayBuffer> | null | undefined
  if (hasBody) {
    try { body = await readBody(request) } catch { return unavailable() }
    if (body === null) return tooLarge()
  }

  const deadline = Date.now() + coldStartBudgetMs
  const repeatable = repeatableAfterNetworkError(request.method, url.pathname)
  let response: Response
  try {
    for (let attempt = 0; ; attempt++) {
      const delay = retryDelaysMs[attempt]
      // Checked once the attempt has ended: out of retries, or the pause would end past the budget.
      const canRetry = () => delay !== undefined && Date.now() + delay < deadline
      let answer: Response | 'timeout'
      try {
        answer = await fetchBefore(deadline, target, {
          method: request.method,
          headers,
          body,
          // Pass redirects to the browser instead of following them here, so Set-Cookie and status reach the client.
          redirect: 'manual',
        })
      } catch (error) {
        // The connection dropped, as it can while a sleeping host starts.
        if (!repeatable || !canRetry()) throw error
        await new Promise(resolve => setTimeout(resolve, delay))
        continue
      }
      if (answer === 'timeout') return gatewayTimeout()
      response = answer
      if (!canRetry() || !await heldBackByPlatform(response)) break
      await response.body?.cancel()
      await new Promise(resolve => setTimeout(resolve, delay))
    }
    if (await platformErrorInsteadOfApi(response)) {
      await response.body?.cancel()
      return wakingUp()
    }
  } catch {
    return unavailable()
  }

  const responseHeaders = forwardable(response.headers)
  // Absolute redirects to the API origin are rewritten to this site so the origin is not exposed.
  const location = responseHeaders.get('location')
  if (location) {
    const next = new URL(location, target)
    if (next.origin === origin.origin) responseHeaders.set('location', url.origin + next.pathname + next.search + next.hash)
  }
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers: responseHeaders })
}
