import { env } from '@/shared/config/env'

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, body: unknown) {
    super(typeof body === 'object' && body !== null && 'detail' in body ? String(body.detail) : fallbackMessage(status))
    this.status = status
    this.body = body
  }
}

/** For answers without their own explanation: the API's empty 401/403 bodies, or a proxy's HTML error page. */
function fallbackMessage(status: number) {
  if (status === 401) return 'Your session has expired. Please log in again.'
  if (status === 403) return 'You don’t have access to this.'
  if (wakingStatuses.includes(status)) return 'The server is waking up. Please wait a few seconds and try again.'
  return 'We couldn’t complete this request. Please try again.'
}

/** The statuses a free-tier host and the proxy in front of it answer with while the host starts. */
const wakingStatuses = [502, 503, 504]

/**
 * True for the proxy's "Server is waking up" answer, its 504 after a minute's wait, or a bare platform 502/503 page.
 * The API's own JSON 503 (database unavailable) is not one of them.
 */
export function isWakingUp(error: unknown) {
  if (!(error instanceof ApiError) || !wakingStatuses.includes(error.status)) return false
  if (error.status === 504 || typeof error.body !== 'object' || error.body === null) return true
  return 'title' in error.body && error.body.title === 'Server is waking up'
}

let lastWakePing = Number.NEGATIVE_INFINITY

/**
 * Starts a sleeping API host now: a no-cors GET straight to its liveness route, so the ~55 s cold start overlaps the
 * user's typing instead of following the first proxied request. Fire-and-forget, the route does not touch the database,
 * and at most one ping per 30 s.
 */
export function wakeApiHost(url = env.apiWakeUrl) {
  if (!url || Date.now() - lastWakePing < 30_000) return
  lastWakePing = Date.now()
  fetch(url, { mode: 'no-cors', cache: 'no-store', credentials: 'omit' }).catch(() => undefined)
}

/** Requests a page cannot work without; while the host wakes they wait a little longer instead of failing. */
const wakeRetryPaths = ['/auth/csrf', '/auth/login', '/auth/me']
const wakeRetryDelaysMs = [2_000, 5_000]

function pause(ms: number, signal: AbortSignal | null | undefined) {
  return new Promise<void>((resolve, reject) => {
    if (signal?.aborted) { reject(signal.reason); return }
    const abort = () => { clearTimeout(timer); reject(signal?.reason) }
    const timer = setTimeout(() => { signal?.removeEventListener('abort', abort); resolve() }, ms)
    signal?.addEventListener('abort', abort, { once: true })
  })
}

/**
 * Sends the request again after a 502/503/504 or a dropped connection, at most twice, for the paths above. Sign-in is
 * safe to repeat: a second sign-in only issues a fresh session. The caller's abort and the timeout are not retried.
 */
async function fetchWhileWaking(path: string, request: () => Promise<Response>, signal: AbortSignal | null | undefined) {
  const delays = wakeRetryPaths.includes(path) ? wakeRetryDelaysMs : []
  for (let attempt = 0; ; attempt++) {
    const delay = delays[attempt]
    if (delay === undefined) return request()
    try {
      const response = await request()
      if (!wakingStatuses.includes(response.status)) return response
      await response.body?.cancel()
    } catch (error) {
      // fetch rejects with a TypeError when the connection fails.
      if (!(error instanceof TypeError)) throw error
    }
    await pause(delay, signal)
  }
}

const sessionExpiredListeners = new Set<() => void>()

/**
 * Called when a request outside /auth/* is answered with 401: the session cookie expired or was revoked (logout in
 * another tab, a removed session). /auth/* is left out: there a 401 means wrong credentials (login) or is already
 * handled (/auth/me, logout). Returns the unsubscribe function.
 */
export function onSessionExpired(listener: () => void) {
  sessionExpiredListeners.add(listener)
  return () => { sessionExpiredListeners.delete(listener) }
}

let csrfRequest: Promise<{ token: string }> | null = null

// Writes that start together share one token request. Separate requests on a session without the antiforgery
// cookie each set a new cookie, and every write but the last then fails the security check.
function csrfToken() {
  csrfRequest ??= apiFetch<{ token: string }>('/auth/csrf').finally(() => { csrfRequest = null })
  return csrfRequest
}

/** The API's answer when the antiforgery check fails; the request was rejected before it did anything. */
const isCsrfFailure = (error: unknown) => error instanceof ApiError && error.status === 400
  && typeof error.body === 'object' && error.body !== null && 'title' in error.body && error.body.title === 'Security check failed'

/**
 * A Cloudflare challenge page ("Just a moment...") in front of the API host instead of the API's answer. Cloudflare
 * answered it, so the request never reached the API and sending it again is safe, writes included.
 */
export class ServerChallengeError extends ApiError {
  constructor(status: number) {
    super(status, null)
    this.message = 'The server’s security check (Render/Cloudflare) held this request back. Wait a moment, then retry.'
  }
}

const challengeMarkers = ['<title>Just a moment...</title>', 'challenge-platform', '__cf_chl', 'Just a moment...']

/** Cloudflare marks challenges with `cf-mitigated: challenge`; the page's own markers catch it when the header is lost. */
export function isChallengePage(headers: Headers, body: unknown) {
  if (headers.get('cf-mitigated')?.toLowerCase() === 'challenge') return true
  return typeof body === 'string' && (headers.get('content-type') ?? '').includes('text/html')
    && challengeMarkers.some(marker => body.includes(marker))
}

type Held = { retry: () => void; cancel: () => void }
const heldRequests = new Set<Held>()
const heldListeners = new Set<() => void>()
const notifyHeld = () => { for (const listener of heldListeners) listener() }

/** How many requests wait behind a server challenge; for useSyncExternalStore. */
export const challenge = {
  subscribe(listener: () => void) {
    heldListeners.add(listener)
    return () => { heldListeners.delete(listener) }
  },
  count: () => heldRequests.size,
  /** Sends every held request again; each one settles its original caller, or waits again if still challenged. */
  retry() { for (const held of [...heldRequests]) held.retry() },
  /** Gives up: every held request fails with the challenge error, so the pages stop waiting. */
  cancel() { for (const held of [...heldRequests]) held.cancel() },
}

/** Keeps the caller's promise open until the user retries or gives up, or the caller aborts the request. */
function holdUntilRetried<T>(again: () => Promise<T>, error: ServerChallengeError, signal: AbortSignal | null | undefined) {
  return new Promise<T>((resolve, reject) => {
    if (signal?.aborted) { reject(signal.reason); return }
    const release = () => { heldRequests.delete(held); signal?.removeEventListener('abort', abort); notifyHeld() }
    const abort = () => { release(); reject(signal?.reason) }
    const held: Held = {
      retry: () => { release(); again().then(resolve, reject) },
      cancel: () => { release(); reject(error) },
    }
    signal?.addEventListener('abort', abort, { once: true })
    heldRequests.add(held)
    notifyHeld()
  })
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  try { return await attempt<T>(path, init) }
  catch (error) {
    // Sign-in, sign-up and the session check show no challenge banner: the proxy already waited out the cold start,
    // so their callers (the form's spinner, the reconnect screen) report what is left as an ordinary failure.
    if (!(error instanceof ServerChallengeError) || path.startsWith('/auth/')) throw error
    return holdUntilRetried(() => apiFetch<T>(path, init), error, init?.signal)
  }
}

async function attempt<T>(path: string, init?: RequestInit): Promise<T> {
  if (!init?.method || ['GET', 'HEAD'].includes(init.method.toUpperCase())) return send<T>(path, init)
  // Fetch for each write: antiforgery tokens are tied to the current identity.
  try { return await send<T>(path, init, (await csrfToken()).token) }
  catch (error) {
    // The cookie changed under the token (another tab, a new sign-in); one retry with a fresh token is safe.
    if (!isCsrfFailure(error)) throw error
    return send<T>(path, init, (await csrfToken()).token)
  }
}

async function send<T>(path: string, init: RequestInit | undefined, csrf?: string): Promise<T> {
  const headers = new Headers(init?.headers)
  if (init?.body) headers.set('Content-Type', 'application/json')
  if (csrf) headers.set('X-CSRF-TOKEN', csrf)
  const response = await fetchWhileWaking(path, () => fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    credentials: 'same-origin',
    // A free-tier API host may need about a minute to wake from idle before the first response.
    signal: init?.signal ?? AbortSignal.timeout(90_000),
    headers,
  }), init?.signal)
  if (wakingStatuses.includes(response.status)) wakeApiHost()
  if (response.status === 204) return undefined as T
  const body = response.headers.get('content-type')?.includes('json')
    ? await response.json()
    : await response.text()

  if (isChallengePage(response.headers, body)) throw new ServerChallengeError(response.status)
  if (response.status === 401 && !path.startsWith('/auth/')) for (const listener of sessionExpiredListeners) listener()
  if (!response.ok) throw new ApiError(response.status, body)
  return body as T
}
