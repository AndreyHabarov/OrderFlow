import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Cart } from './types'
import { useCart } from './useCart'

const cartWith = (count: number): Cart => ({
  items: Array.from({ length: count }, (_, i) => ({ productId: `p${i}`, quantity: 1, unitPrice: 1, lineTotal: 1 })),
  total: count,
  currency: 'USD',
})

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

describe('useCart', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    fetchMock.mockReset()
    vi.unstubAllGlobals()
  })

  it('applies only the most recently issued read when responses arrive out of order', async () => {
    const resolvers: Array<(response: Response) => void> = []
    fetchMock.mockImplementation(() => new Promise<Response>((resolve) => resolvers.push(resolve)))

    const { result } = renderHook(() => useCart(false))

    let first: Promise<void>
    let second: Promise<void>
    act(() => {
      first = result.current.reload() // issued first, would show 1 item
      second = result.current.reload() // issued last, the truth: 3 items
    })

    await act(async () => {
      resolvers[1](json(cartWith(3))) // the newer read finishes first
      await second
      resolvers[0](json(cartWith(1))) // the stale one arrives late and must be ignored
      await first
    })

    expect(result.current.cart?.items).toHaveLength(3)
  })

  it('ignores reads that finish after the cart was cleared (sign out)', async () => {
    let resolve!: (response: Response) => void
    fetchMock.mockImplementation(() => new Promise<Response>((r) => (resolve = r)))
    const { result } = renderHook(() => useCart(false))

    let pending: Promise<void>
    act(() => {
      pending = result.current.reload()
    })
    act(() => result.current.clear())
    await act(async () => {
      resolve(json(cartWith(2)))
      await pending
    })

    expect(result.current.cart).toBeNull()
  })
})
