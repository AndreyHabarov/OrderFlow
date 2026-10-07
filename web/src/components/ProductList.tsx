import { useEffect, useState } from 'react'
import { request } from '../api'
import { errorMessage, money } from '../format'
import type { Paged, Product } from '../types'

interface Props {
  signedIn: boolean
  onNeedSignIn: () => void
  onCartChanged: () => void
}

export function ProductList({ signedIn, onNeedSignIn, onCartChanged }: Props) {
  const [page, setPage] = useState(1)
  const [data, setData] = useState<Paged<Product> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    request<Paged<Product>>(`/api/products?page=${page}&pageSize=8`)
      .then((result) => {
        if (!cancelled) {
          setData(result)
          setError(null)
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(errorMessage(err))
      })
    return () => {
      cancelled = true
    }
  }, [page])

  async function addToCart(product: Product) {
    if (!signedIn) {
      onNeedSignIn()
      return
    }
    setAdding(product.id)
    setNotice(null)
    try {
      await request('/api/cart/items', { method: 'POST', body: { productId: product.id, quantity: 1 } })
      onCartChanged()
      setNotice(`Added “${product.name}” to the cart.`)
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setAdding(null)
    }
  }

  if (error && !data) return <p className="error" role="alert">{error}</p>
  if (!data) return <p className="hint">Loading products…</p>

  return (
    <section>
      <h2>Products</h2>
      {notice && <p className="notice" role="status">{notice}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <ul className="grid">
        {data.items.map((product) => (
          <li key={product.id} className="card product">
            <h3>{product.name}</h3>
            <p className="hint">{product.description}</p>
            <p className="price">{money(product.price, product.currency)}</p>
            <button type="button" disabled={adding === product.id} onClick={() => void addToCart(product)}>
              {adding === product.id ? 'Adding…' : 'Add to cart'}
            </button>
          </li>
        ))}
      </ul>
      {data.totalPages > 1 && (
        <nav className="pager" aria-label="Pagination">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Previous
          </button>
          <span>
            Page {data.page} of {data.totalPages}
          </span>
          <button type="button" disabled={page >= data.totalPages} onClick={() => setPage(page + 1)}>
            Next
          </button>
        </nav>
      )}
    </section>
  )
}
