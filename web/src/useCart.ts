import { useCallback, useEffect, useRef, useState } from 'react'
import { request } from './api'
import type { Cart } from './types'

/**
 * Keeps the cart in sync with the server.
 *
 * Several mutations can be in flight at once (for example quick clicks on different products). Each response is only
 * a snapshot from the moment its request ran, and responses may arrive out of order, so the UI never trusts a mutation
 * response. Instead it re-reads the cart, and only the most recently issued read is applied.
 */
export function useCart(signedIn: boolean) {
  const [cart, setCart] = useState<Cart | null>(null)
  const latestRead = useRef(0)

  /** Reads the cart; resolves to undefined when the read failed or a newer read was issued meanwhile. */
  const read = useCallback(async (): Promise<Cart | undefined> => {
    const readId = ++latestRead.current
    try {
      const result = await request<Cart>('/api/cart')
      return readId === latestRead.current ? result : undefined
    } catch {
      return undefined // the badge is informational; views that act on the cart report their own errors
    }
  }, [])

  const reload = useCallback(async () => {
    const result = await read()
    if (result) setCart(result)
  }, [read])

  const clear = useCallback(() => {
    latestRead.current++ // invalidates reads that are still in flight
    setCart(null)
  }, [])

  useEffect(() => {
    if (!signedIn) return
    let active = true
    void read().then((result) => {
      if (active && result) setCart(result)
    })
    return () => {
      active = false
    }
  }, [signedIn, read])

  return { cart, reload, clear }
}
