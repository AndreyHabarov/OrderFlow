# ADR 0004: Idempotent order creation with a unique key in PostgreSQL

- Status: accepted
- Date: 2026-10-07
- Supersedes: the plan's earlier idea of keeping idempotency keys in Redis

## Context
Clients retry `POST /orders` after timeouts and network errors. A retry must never create a second order or reserve stock twice. The original plan stored idempotency keys in Redis.

## Options considered
1. **Redis key per request (`SET NX` with TTL).** Fast, but Redis is a cache: a flush, eviction or failover can drop the key and allow a duplicate. The check and the order insert are two systems, so there is always a gap between them.
2. **Separate `idempotency_keys` table with the stored response.** Durable, supports "same key, different payload" detection, but adds a table, response storage and cleanup.
3. **Store the key on the order and add a unique index `(customer_id, idempotency_key)`.** The database enforces uniqueness in the same transaction that creates the order and reserves the stock.

## Decision
Option 3. `Order.IdempotencyKey` is nullable and unique per customer (PostgreSQL treats NULLs as distinct, so keyless orders are unaffected). The `Idempotency-Key` header is required on `POST /orders`.

Flow:
1. Look up an order by `(customer, key)`; if found, return it with `200 OK` and header `Idempotent-Replayed: true`.
2. Otherwise reserve stock, create the order and empty the cart in one transaction.
3. If two requests race, the unique index rejects the second insert, its whole transaction (including the stock reservation) rolls back, and the handler returns the winner's order.

## Consequences
- Correctness no longer depends on Redis being available or consistent; the guarantee is as strong as the database transaction.
- A replay returns the order as it is now, not the original response bytes (the status may have advanced). Acceptable here.
- "Same key with a different cart" is not detected: checkout has no request body, and the cart is emptied after success. Revisit if endpoints with bodies need idempotency.
- Keys live as long as orders; no cleanup job is needed.
- Redis keeps only the catalog cache (ADR 0003).

## Interview angle
"How do you make a POST idempotent? Why not Redis? What happens if two identical requests arrive at the same time? How do you handle the unique violation, and what happens to the work the losing request already did?"
