import { useCallback, useState } from 'react'
import { useAuth } from './auth-context'
import { AuthForm } from './components/AuthForm'
import { CartView } from './components/CartView'
import { OrdersView } from './components/OrdersView'
import { ProductList } from './components/ProductList'
import { useCart } from './useCart'

type View = 'shop' | 'cart' | 'orders' | 'auth'

export default function App() {
  const { user, ready, logout } = useAuth()
  const [view, setView] = useState<View>('shop')
  const { cart, reload: reloadCart, clear: clearCart } = useCart(!!user)
  const [ordersVersion, setOrdersVersion] = useState(0)

  const signOut = useCallback(async () => {
    await logout()
    clearCart()
    setView('shop')
  }, [logout, clearCart])

  if (!ready) return <p className="hint center">Loading…</p>

  // Cart and orders need a session; without one the corresponding tabs lead to the sign-in form.
  const effectiveView: View = !user && (view === 'cart' || view === 'orders') ? 'auth' : view
  const itemCount = cart?.items.reduce((sum, item) => sum + item.quantity, 0) ?? 0

  return (
    <div className="app">
      <header className="topbar">
        <strong className="brand">OrderFlow</strong>
        <nav aria-label="Main">
          <button type="button" aria-current={effectiveView === 'shop'} onClick={() => setView('shop')}>
            Shop
          </button>
          <button type="button" aria-current={effectiveView === 'cart'} onClick={() => setView('cart')}>
            Cart{itemCount > 0 ? ` (${itemCount})` : ''}
          </button>
          <button type="button" aria-current={effectiveView === 'orders'} onClick={() => setView('orders')}>
            Orders
          </button>
        </nav>
        <div className="account">
          {user ? (
            <>
              <span className="hint">{user.email}</span>
              <button type="button" className="link" onClick={() => void signOut()}>
                Sign out
              </button>
            </>
          ) : (
            <button type="button" onClick={() => setView('auth')}>
              Sign in
            </button>
          )}
        </div>
      </header>

      <main>
        {effectiveView === 'shop' && (
          <ProductList signedIn={!!user} onNeedSignIn={() => setView('auth')} onCartChanged={() => void reloadCart()} />
        )}
        {effectiveView === 'cart' && (
          <CartView cart={cart} onCartChanged={() => void reloadCart()} onOrdered={() => setOrdersVersion((v) => v + 1)} />
        )}
        {effectiveView === 'orders' && <OrdersView refreshKey={ordersVersion} />}
        {effectiveView === 'auth' && <AuthForm onDone={() => setView('shop')} />}
      </main>
    </div>
  )
}
