import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getRefreshToken, request, setSession } from './api'
import type { AuthResponse } from './types'

const auth = (suffix: string): AuthResponse => ({
  accessToken: `access-${suffix}`,
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  refreshToken: `refresh-${suffix}`,
  user: { id: '1', email: 'a@example.com', role: 'Customer' },
})

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('api client', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    sessionStorage.clear()
    setSession(null)
  })

  afterEach(() => {
    fetchMock.mockReset()
    vi.unstubAllGlobals()
  })

  it('sends the access token and idempotency key', async () => {
    setSession(auth('1'))
    fetchMock.mockResolvedValueOnce(json({ ok: true }))

    await request('/api/orders', { method: 'POST', idempotencyKey: 'key-1' })

    const init = fetchMock.mock.calls[0][1]!
    const headers = init.headers as Record<string, string>
    expect(headers.Authorization).toBe('Bearer access-1')
    expect(headers['Idempotency-Key']).toBe('key-1')
  })

  it('refreshes the session once on 401 and retries the request', async () => {
    setSession(auth('1'))
    fetchMock
      .mockResolvedValueOnce(json({ title: 'Unauthorized' }, 401))
      .mockResolvedValueOnce(json(auth('2')))
      .mockResolvedValueOnce(json({ items: [] }))

    const result = await request<{ items: unknown[] }>('/api/cart')

    expect(result.items).toEqual([])
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchMock.mock.calls[1][0]).toBe('/api/auth/refresh')
    const retryHeaders = fetchMock.mock.calls[2][1]!.headers as Record<string, string>
    expect(retryHeaders.Authorization).toBe('Bearer access-2')
    expect(getRefreshToken()).toBe('refresh-2')
  })

  it('shares one refresh request between concurrent 401s (the refresh token is single-use)', async () => {
    setSession(auth('1'))
    let refreshCalls = 0
    fetchMock.mockImplementation((input, init) => {
      const url = String(input)
      if (url === '/api/auth/refresh') {
        refreshCalls++
        return Promise.resolve(json(auth('2')))
      }
      const headers = (init?.headers ?? {}) as Record<string, string>
      const bearer = headers.Authorization
      return Promise.resolve(bearer === 'Bearer access-2' ? json({ ok: true }) : json({}, 401))
    })

    await Promise.all([request('/api/cart'), request('/api/orders'), request('/api/cart')])

    expect(refreshCalls).toBe(1)
  })

  it('clears the session and fails when the refresh is rejected', async () => {
    setSession(auth('1'))
    fetchMock.mockResolvedValueOnce(json({}, 401)).mockResolvedValueOnce(json({ title: 'Unauthorized' }, 401))

    await expect(request('/api/cart')).rejects.toMatchObject({ status: 401 })
    expect(getRefreshToken()).toBeNull()
  })

  it('turns problem details into an ApiError with a readable message', async () => {
    fetchMock.mockResolvedValueOnce(json({ title: 'Business rule violated', detail: 'The cart is empty.' }, 422))

    const error = await request('/api/orders', { method: 'POST' }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(422)
    expect((error as ApiError).message).toBe('The cart is empty.')
  })

  it('uses the first validation message when there is no detail', async () => {
    fetchMock.mockResolvedValueOnce(json({ title: 'Validation failed', errors: { Quantity: ['Quantity must be between 1 and 100.'] } }, 400))

    await expect(request('/api/cart/items', { method: 'POST', body: {} })).rejects.toThrow('Quantity must be between 1 and 100.')
  })
})
