import { useEffect, useState } from 'react'
import { request } from '../api'
import { dateTime, errorMessage, money } from '../format'
import type { Order } from '../types'

export function OrdersView({ refreshKey }: { refreshKey: number }) {
  const [orders, setOrders] = useState<Order[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    request<Order[]>('/api/orders')
      .then((result) => {
        if (!cancelled) setOrders(result)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(errorMessage(err))
      })
    return () => {
      cancelled = true
    }
  }, [refreshKey])

  if (error) return <p className="error" role="alert">{error}</p>
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
