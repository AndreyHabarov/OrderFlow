import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { getRefreshToken, getSessionUser, refreshSession, request, setSession, setSessionLostHandler } from './api'
import { AuthContext } from './auth-context'
import type { AuthResponse, User } from './types'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    setSessionLostHandler(() => setUser(null))

    let cancelled = false
    const restore = getRefreshToken() ? refreshSession() : Promise.resolve(false)
    void restore.then((ok) => {
      if (cancelled) return
      setUser(ok ? getSessionUser() : null)
      setReady(true)
    })

    return () => {
      cancelled = true
      setSessionLostHandler(null)
    }
  }, [])

  const authenticate = useCallback(async (path: string, email: string, password: string) => {
    const auth = await request<AuthResponse>(path, { method: 'POST', body: { email, password } })
    setSession(auth)
    setUser(auth.user)
  }, [])

  const login = useCallback((email: string, password: string) => authenticate('/api/auth/login', email, password), [authenticate])
  const register = useCallback((email: string, password: string) => authenticate('/api/auth/register', email, password), [authenticate])

  const logout = useCallback(async () => {
    const refreshToken = getRefreshToken()
    setSession(null)
    setUser(null)
    if (refreshToken) {
      try {
        await request<void>('/api/auth/logout', { method: 'POST', body: { refreshToken } })
      } catch {
        // the local session is already gone; a failed revoke only leaves the token to expire
      }
    }
  }, [])

  const value = useMemo(() => ({ user, ready, login, register, logout }), [user, ready, login, register, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
