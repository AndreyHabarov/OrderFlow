# OrderFlow web

React 19 + Vite + TypeScript client for the OrderFlow API.

```bash
npm install
npm run dev      # http://localhost:5173, proxies /api to http://127.0.0.1:8085 (set API_URL to change)
npm run build    # type-check and production build
npm run lint     # oxlint
npm test         # vitest
```

Screens: shop (paged catalog, add to cart), cart (place order), orders, sign in / register.

Notes:
- The access token lives in memory; the refresh token is kept in `sessionStorage` (see ADR 0005 for the trade-off). A 401 triggers one shared refresh and a retry.
- Placing an order sends an `Idempotency-Key`. The key is reused when a retry follows a network error or a 5xx, and replaced after a business rejection (see `components/CartView.tsx`).
- The cart is never taken from a mutation response; it is re-read and only the latest read is applied (see `useCart.ts`).
