import { useEffect, useRef, useState } from 'react'
import { ApiError, request } from '../api'
import { errorMessage, money } from '../format'
import type { Cart, Order, Product } from '../types'

interface Props {
  cart: Cart | null
  onCartChanged: () => void
  onOrdered: () => void
}

/** Product names for cart lines. The server caches single-product reads, so this is cheap. */
function useProductNames(productIds: string[]): Record<string, string> {
  const [names, setNames] = useState<Record<string, string>>({})
  const key = productIds.join(',')

  useEffect(() => {
    const missing = productIds.filter((id) => !(id in names))
    if (missing.length === 0) return
    let cancelled = false
    void Promise.all(
      missing.map((id) =>
        request<Product>(`/api/products/${id}`)
          .then((p) => [id, p.name] as const)
          .catch(() => [id, 'Unknown product'] as const),
      ),
    ).then((pairs) => {
      if (!cancelled) setNames((previous) => ({ ...previous, ...Object.fromEntries(pairs) }))
    })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `key` captures productIds; `names` is read intentionally stale
  }, [key])

  return names
}

export function CartView({ cart, onCartChanged, onOrdered }: Props) {
  const names = useProductNames(cart?.items.map((i) => i.productId) ?? [])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [placed, setPlaced] = useState<Order | null>(null)

  // One key per checkout attempt. Retrying after a network error or a 5xx reuses it, so the server cannot create a
  // second order; a business rejection (4xx other than 409) starts a fresh attempt.
  const idempotencyKey = useRef<string | null>(null)

  async function remove(productId: string) {
    setError(null)
    try {
      await request(`/api/cart/items/${productId}`, { method: 'DELETE' })
      onCartChanged()
    } catch (err) {
      setError(errorMessage(err))
    }
  }

  async function checkout() {
    setBusy(true)
    setError(null)
    idempotencyKey.current ??= crypto.randomUUID()
    try {
      const order = await request<Order>('/api/orders', { method: 'POST', idempotencyKey: idempotencyKey.current })
      idempotencyKey.current = null
      setPlaced(order)
      onCartChanged()
      onOrdered()
    } catch (err) {
      if (err instanceof ApiError && err.status >= 400 && err.status < 500 && err.status !== 409) {
        idempotencyKey.current = null
      }
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  if (placed) {
    return (
      <section className="card">
        <h2>Thank you!</h2>
        <p>
          Order <code>{placed.id.slice(0, 8)}</code> is <strong>{placed.status}</strong>. Total {money(placed.total, placed.currency)}.
        </p>
        <button type="button" onClick={() => setPlaced(null)}>
          Continue shopping
        </button>
      </section>
    )
  }

  if (!cart || cart.items.length === 0) return <p className="hint">Your cart is empty.</p>

  return (
    <section>
      <h2>Cart</h2>
      <table className="table">
        <thead>
          <tr>
            <th>Product</th>
            <th>Qty</th>
            <th>Price</th>
            <th>Total</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {cart.items.map((item) => (
            <tr key={item.productId}>
              <td>{names[item.productId] ?? '…'}</td>
              <td>{item.quantity}</td>
              <td>{money(item.unitPrice, cart.currency)}</td>
              <td>{money(item.lineTotal, cart.currency)}</td>
              <td>
                <button type="button" className="link" onClick={() => void remove(item.productId)}>
                  Remove
                </button>
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th colSpan={3}>Total</th>
            <th>{money(cart.total, cart.currency)}</th>
            <th />
          </tr>
        </tfoot>
      </table>
      {error && <p className="error" role="alert">{error}</p>}
      <button type="button" disabled={busy} onClick={() => void checkout()}>
        {busy ? 'Placing order…' : 'Place order'}
      </button>
    </section>
  )
}
