import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Order } from '../types'
import { OrdersView } from './OrdersView'

vi.mock('../api', () => ({ request: vi.fn() }))
import { request } from '../api'

const order = (status: string, extra: Partial<Order> = {}): Order => ({
  id: '11111111-aaaa-bbbb-cccc-222222222222',
  status,
  total: 50,
  currency: 'USD',
  createdAt: '2026-10-07T12:00:00Z',
  cancellationReason: null,
  items: [{ productId: 'p1', productName: 'Keyboard', quantity: 1, unitPrice: 50, lineTotal: 50 }],
  ...extra,
})

describe('OrdersView', () => {
  const requestMock = vi.mocked(request)

  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    cleanup() // the library only auto-cleans with test globals, which this project does not enable
    vi.useRealTimers()
    requestMock.mockReset()
  })

  it('keeps polling while an order is in progress and stops when it is final', async () => {
    requestMock
      .mockResolvedValueOnce([order('Pending')])
      .mockResolvedValueOnce([order('StockReserved')])
      .mockResolvedValue([order('Confirmed')])

    render(<OrdersView refreshKey={0} />)
    await act(() => vi.advanceTimersByTimeAsync(0))
    expect(screen.getByText('Pending')).toBeInTheDocument()

    await act(() => vi.advanceTimersByTimeAsync(2000))
    expect(screen.getByText('StockReserved')).toBeInTheDocument()

    await act(() => vi.advanceTimersByTimeAsync(2000))
    expect(screen.getByText('Confirmed')).toBeInTheDocument()
    const callsWhenFinal = requestMock.mock.calls.length

    await act(() => vi.advanceTimersByTimeAsync(10_000))
    expect(requestMock.mock.calls.length).toBe(callsWhenFinal) // no more polling once everything is final
  })

  it('shows why an order was cancelled and does not poll for it', async () => {
    requestMock.mockResolvedValue([order('Cancelled', { cancellationReason: 'Payment failed: The bank declined the payment.' })])

    render(<OrdersView refreshKey={0} />)
    await act(() => vi.advanceTimersByTimeAsync(0))

    expect(screen.getByText(/The bank declined the payment/)).toBeInTheDocument()
    await act(() => vi.advanceTimersByTimeAsync(10_000))
    expect(requestMock).toHaveBeenCalledTimes(1)
  })
})
