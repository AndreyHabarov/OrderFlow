import type { AuthResponse, User } from './types'

const REFRESH_KEY = 'orderflow.refreshToken'

/** An error response from the API (RFC 7807 problem details) with a readable message. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

// The access token lives only in memory; the refresh token lives in sessionStorage so a reload keeps the session,
// but closing the tab ends it. Trade-off (documented in ADR 0005): script injection could read sessionStorage.
let accessToken: string | null = null
let sessionUser: User | null = null
let refreshInFlight: Promise<boolean> | null = null
let onSessionLost: (() => void) | null = null

export function getRefreshToken(): string | null {
  try {
    return sessionStorage.getItem(REFRESH_KEY)
  } catch {
    return null
  }
}

export function setSession(auth: AuthResponse | null): void {
  accessToken = auth?.accessToken ?? null
  sessionUser = auth?.user ?? null
  try {
    if (auth) sessionStorage.setItem(REFRESH_KEY, auth.refreshToken)
    else sessionStorage.removeItem(REFRESH_KEY)
  } catch {
    // storage may be unavailable (private mode); the session then lasts until reload
  }
}

export function setSessionLostHandler(handler: (() => void) | null): void {
  onSessionLost = handler
}

async function parseError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> }
    const fieldErrors = problem.errors ?? {}
    const firstField = Object.values(fieldErrors)[0]?.[0]
    return new ApiError(response.status, problem.detail ?? firstField ?? problem.title ?? response.statusText, fieldErrors)
  } catch {
    return new ApiError(response.status, response.statusText || 'Request failed')
  }
}

/** Exchanges the refresh token for a new pair. Concurrent callers share one request (the token is single-use). */
export function refreshSession(): Promise<boolean> {
  const refreshToken = getRefreshToken()
  if (!refreshToken) return Promise.resolve(false)

  refreshInFlight ??= (async () => {
    const response = await fetch('/api/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
    if (!response.ok) {
      setSession(null)
      onSessionLost?.()
      return false
    }
    setSession((await response.json()) as AuthResponse)
    return true
  })().finally(() => {
    refreshInFlight = null
  })

  return refreshInFlight
}

interface RequestOptions {
  method?: string
  body?: unknown
  idempotencyKey?: string
}

export async function request<T>(path: string, options: RequestOptions = {}, canRetry = true): Promise<T> {
  const headers: Record<string, string> = {}
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey

  const response = await fetch(path, {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })

  if (response.status === 401 && canRetry && (await refreshSession())) {
    return request<T>(path, options, false)
  }

  if (!response.ok) throw await parseError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export function getSessionUser(): User | null {
  return sessionUser
}
