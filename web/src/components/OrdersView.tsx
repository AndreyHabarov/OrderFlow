import { useEffect, useState } from 'react'
import { request } from '../api'
import { dateTime, errorMessage, money } from '../format'
import type { Order } from '../types'

/** Statuses an order passes through while Inventory and Payments are still working on it. */
const IN_PROGRESS = new Set(['Pending', 'StockReserved', 'Paid'])
const POLL_INTERVAL_MS = 2000

export function OrdersView({ refreshKey }: { refreshKey: number }) {
  const [orders, setOrders] = useState<Order[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pollRound, setPollRound] = useState(0)

  useEffect(() => {
    let cancelled = false
    request<Order[]>('/api/orders')
      .then((result) => {
        if (!cancelled) {
          setOrders(result)
          setError(null)
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(errorMessage(err))
      })
    return () => {
      cancelled = true
    }
  }, [refreshKey, pollRound])

  // Order status changes happen asynchronously in other services. Until SignalR pushes updates (stage 4), re-read
  // the list every couple of seconds while any order is still in progress, and stop once all are final.
  const inProgress = orders?.some((order) => IN_PROGRESS.has(order.status)) ?? false
  useEffect(() => {
    if (!inProgress) return
    const timer = setTimeout(() => setPollRound((round) => round + 1), POLL_INTERVAL_MS)
    return () => clearTimeout(timer)
  }, [inProgress, orders])

  if (error && !orders) return <p className="error" role="alert">{error}</p>
  if (!orders) return <p className="hint">Loading orders…</p>
  if (orders.length === 0) return <p className="hint">You have no orders yet.</p>

  return (
    <section>
      <h2>My orders</h2>
      <ul className="orders">
        {orders.map((order) => (
          <li key={order.id} className="card">
            <header>
              <code>{order.id.slice(0, 8)}</code>
              <span className={`badge badge-${order.status.toLowerCase()}`}>{order.status}</span>
              <span className="hint">{dateTime(order.createdAt)}</span>
              <strong>{money(order.total, order.currency)}</strong>
            </header>
            <ul>
              {order.items.map((item) => (
                <li key={item.productId}>
                  {item.quantity} × {item.productName} — {money(item.lineTotal, order.currency)}
                </li>
              ))}
            </ul>
            {order.cancellationReason && <p className="error">Cancelled: {order.cancellationReason}</p>}
          </li>
        ))}
      </ul>
    </section>
  )
}
