# ADR 0003: MediatR pinned to 12.5.0 and cache-aside with versioned namespaces

- Status: accepted
- Date: 2026-10-07

## Context
Stage 1 introduces CQRS handlers and the first cache. Two decisions came up: which mediator library to use (MediatR changed its license from 13.0 onward) and how to invalidate cached catalog data without scanning Redis.

## Options considered

### Mediator
1. **MediatR 13+/14.** Commercial license terms (registration/key required, community tier with conditions). Needs a legal check before use.
2. **MediatR 12.5.0.** Last Apache-2.0 release, same API, no key.
3. **Source-generated `Mediator` library or a hand-written dispatcher.** No licensing question; different API, more learning cost or more code.

### Cache invalidation
1. **Delete keys by pattern (`SCAN` + `DEL`).** O(N) over the keyspace; unsafe on large instances and not atomic.
2. **Per-key TTL only.** Simple, but stale data for up to the TTL after a change.
3. **Versioned namespaces.** Keys are `cache:{namespace}:{version}:{key}`; invalidation is `INCR cache:version:{namespace}`, O(1) and atomic. Old keys are never read again and expire by TTL.

## Decision
- MediatR **12.5.0**, pinned in `Directory.Packages.props`. Revisit when a future task needs features only available in newer versions, and re-check the license at that moment.
- Cache-aside through `ICacheService` with **versioned namespaces** plus a short TTL (5 minutes) as a safety net. A Redis failure never fails a request: the service logs a warning and reads from the database. `null` results (for example an unknown product id) are not cached.
- Handlers depend on `IProductRepository` and `ICacheService` abstractions defined in Application, implemented in Infrastructure.

## Consequences
- Orphaned keys of old versions occupy memory until their TTL expires; acceptable for a small catalog.
- Anything that changes product data (price, stock, name) must call `InvalidateNamespaceAsync(CatalogCache.Namespace)`. The order flow in S1-04 will do this when stock is reserved.
- Cache stampede (many requests rebuilding the same key at once) is not handled; acceptable now, candidate for a lock or request coalescing if load tests show it.
- Staying on MediatR 12.x means no security or feature updates from the maintainers.

## Interview angle
"Cache-aside vs read-through vs write-through? How do you invalidate a cache without scanning keys? What happens when Redis is down? What is a cache stampede?"
