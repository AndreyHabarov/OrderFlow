# Interview notes

Working log. For each problem: situation, cause, solution, numbers before/after, what I learned.
Weak spots found in interview rounds are listed per stage.

## Template: problem entry

### <short title>
- **Situation:**
- **Cause:**
- **Solution:**
- **Numbers (before -> after):**
- **What I learned:**

## Stage 0: Foundation

### Questions to be able to answer
- Why a healthcheck, and what does `depends_on: condition: service_healthy` change?
- Liveness vs readiness: what does each check and who consumes it?
- What does a multi-stage Dockerfile do and why does the restore layer come first?
- Why pin image versions instead of `latest`?
- Why central package management and `TreatWarningsAsErrors`?

### Weak spots (from interview rounds)
- _empty_

### Problems and metrics
- _empty_

## Stage 1: Monolith without a broker

### Questions to be able to answer
- Cache-aside vs read-through vs write-through: which one is used here and why?
- How is the catalog cache invalidated without scanning Redis? What happens to old keys?
- What happens to the API when Redis is down? Is that the right behaviour?
- What is a cache stampede and how would you reduce it?
- Why is MediatR pinned to 12.5.0?

### Problems and metrics
- Catalog list endpoint via `127.0.0.1`: about 5 ms with a warm cache (measured locally, 3 requests). Cold-path number to be recorded with k6 in stage 5b.
- Runtime crash `FileNotFoundException: Microsoft.EntityFrameworkCore 10.0.12`: the code compiled against 10.0.12 (Design package) while Npgsql pulled 10.0.4 at runtime. Fixed by pinning EF Core packages explicitly. Lesson: transitive version drift between compile time and runtime.

### Cart and checkout (S1-04)
- Checkout is one database transaction: reserve stock, create order, empty cart. `Product` has a `xmin` row-version token, so two concurrent checkouts of the last item cannot both succeed (the loser gets 409). Truly concurrent behaviour is to be proven by an integration test in S1-08.
- Temporary identity: `X-Customer-Id` header (`HeaderCurrentUser`), replaced by JWT in S1-06. Never expose it outside local development.
- Questions: optimistic vs pessimistic locking and which one fits here; why the order stores a snapshot of name and price; why validation lives in a MediatR pipeline behavior; why `DomainException` maps to 422 and a concurrency conflict to 409.

### Idempotency (S1-05)
- Live test: 8 parallel `POST /orders` with one key gave 1x `201` and 7x `200 Idempotent-Replayed: true`; stock dropped by exactly 3; one order exists.
- Problem found on the way: `docker compose up -d --build` failed silently (I only looked at the last output line) and an old container kept serving traffic, so my first test ran against stale code. Lesson: check the build exit status and the container's uptime/image before trusting a test. Root cause of the build failure: the Docker build context lacked `.editorconfig`, so analyzers ran on the generated migration with default severities and `TreatWarningsAsErrors` failed the Release publish.
- Questions: why the database and not Redis for idempotency; what exactly happens to the losing request's transaction; why a replay returns 200 and not 201; what the limits are (no payload comparison).

### Authentication (S1-06, ADR 0005)
- Live checks: no token 401, tampered token 401, duplicate email 409, wrong password 401, refresh rotates the token, reusing the old refresh token 401 and then the newest one also 401 (all sessions revoked), admin login returns role Admin, full order flow works with a real JWT.
- The container uptime ("Up 3 seconds") is now part of my checklist after every `docker compose up --build`, after the stale-container mistake in S1-05.
- Questions: refresh rotation and reuse detection; why only the hash of the refresh token is stored; HS256 vs RS256 when services are split; what a 15-minute access token costs after logout; why identical errors for wrong password and unknown email; where a browser should keep the refresh token.

### Integration tests (S1-08)
- 8 tests on real PostgreSQL, Redis and RabbitMQ (Testcontainers, same image versions as compose), running in CI.
- Real concurrency proven: 8 parallel checkouts with one idempotency key produce exactly one order and one stock reduction; 12 buyers competing for 5 units never oversell (`stock_after == 5 - created`, only 201/409/422 responses).
- Why Testcontainers instead of EF InMemory or SQLite: unique indexes, `xmin` concurrency tokens and transaction behaviour are PostgreSQL features; a fake database would pass tests the real one fails.
- Questions: what the 409 vs 422 split means for a client; why the invariant is asserted instead of exact counts in a race; why `UseSetting` instead of `ConfigureAppConfiguration` with minimal hosting; how test isolation works with one shared container set.

### Web client and two real bugs found by clicking (S1-09)
1. **Server race (500).** Two quick "Add to cart" clicks by a customer without a cart raced to create the cart; one request hit the unique index on `carts.customer_id` and returned 500. Reproduced first by an integration test (7 of 8 parallel requests failed), then fixed: cart add/remove runs inside `ConflictRetry` (unique violation or `DbUpdateConcurrencyException` -> clear the change tracker, reload, try again with a small back-off), `cart_items` got a unique `(cart_id, product_id)` index, and any unique violation that still escapes maps to 409 instead of 500.
2. **Client race (stale cart).** After the server fix the badge still showed too few items. The server was right; the UI applied the snapshot from whichever mutation response arrived last. Fix: never trust mutation responses, re-read the cart and apply only the most recently issued read (`useCart`, covered by a test with out-of-order promises).
- Lesson: unit tests with in-memory fakes cannot find races; the concurrency tests on real PostgreSQL did, but only after I clicked through the UI and wrote the scenario down. Browser testing found what the API tests had not covered.
- Process mistake: I once ran `git checkout -- .` to "check state" and discarded an uncommitted test. Read-only commands (`git status`, `git diff`) are enough for that.
- Questions: optimistic retry vs pessimistic locking (`SELECT ... FOR UPDATE`) for the cart; why retries need `ChangeTracker.Clear()`; why replacing a record in a collection produces delete+insert and how that conflicts; idempotency key lifecycle on the client; where to keep a refresh token in a browser and why.

### CI caught a race my local runs missed (idempotency, after S1-08)
- Symptom: post-merge CI on `main` failed twice (red crosses on S1-08 and S1-09 commits) in `Parallel_checkouts_with_the_same_idempotency_key...`: 3 of 8 duplicate requests got 422 instead of a replay. Locally the same test passed 5 times in a row; the CI runner is slower, which shifts the timing into other race windows. "It passed 4 times" was luck, not proof.
- Root cause: between "look up the order by key" and "save" there are three windows. (1) The winner commits after our lookup but before we read the cart: we see an empty cart and answered 422 instead of looking again. (2) The winner already updated the product rows: our UPDATE fails the `xmin` version check (409 / `DbUpdateConcurrencyException`) before the unique index on the key is reached. (3) Both reach the unique index (this one was handled from the start).
- Fix: one helper `FindReplayAsync` used at the fast path, when the cart is empty, and in the catch for both unique violation and concurrency conflict; a conflict with no matching key (another customer won the stock) is rethrown as 409. Deterministic unit tests for windows 1 and 2 (they fail on the old handler, pass on the new one), integration burst repeated 10 times.
- Lessons: a green PR check is not a guarantee for timing-dependent code; test the invariant under repetition; reproduce the failure deterministically with fakes when the real race is rare; read CI logs for the actual assertion instead of guessing.
- Questions: list every point where two concurrent duplicates can diverge; why the same logical conflict surfaces as two different exceptions; how you would prove an idempotent endpoint correct (invariants, repetition, fault injection).

## Stage 2: Services and RabbitMQ

### Messaging library (S2-02), questions to be able to answer
- What does a publisher confirm guarantee and what does it not? (Broker accepted and, for persistent messages on durable queues, wrote it; it says nothing about a consumer having processed it.)
- Why `mandatory: true`? What happens to an unroutable message without it? (It is silently dropped.)
- What do `ack`, `nack` with requeue and `nack` without requeue each do, and which one is used for a failing handler vs an unreadable message? Why is requeue-on-failure dangerous for a poison message (it loops; stage 3 adds retry + DLQ)?
- What is prefetch? With prefetch 2 and a busy consumer, where do the other messages wait? (Measured: 5 published, 1 in progress + 1 buffered, 3 stay in the queue.)
- What happens to unacknowledged messages when a consumer dies or the channel closes? Why does a graceful stop cancel the consumer first and then wait for the message in progress?
- Why does the consumer refuse to start when a bound routing key has no handler?
- Why one connection and few channels? Why is a channel not thread-safe and how does the publisher cope (a semaphore)?
- Where do correlation and causation ids live and why not in the JSON body?

### Inventory service (S2-03), questions to be able to answer
- Walk through what happens when `OrderCreated` is delivered twice. (The reservation row keyed by order id is committed together with the stock change, so the second delivery only repeats the answer.)
- Why is the order "commit, then publish, then ack" and what recovers a crash between commit and publish? (Redelivery: the handler finds the reservation and republishes. Tested with a publisher that fails once.)
- Two Inventory instances compete for the same queue. How is overselling prevented? (`xmin` row version on the stock row: the loser gets a concurrency exception, the message is requeued and re-read.) What do the duplicate answers cost downstream? (Consumers must be idempotent.)
- Why does a failed reservation also get a row? (So a redelivered message cannot flip a refusal into a success after stock changes.)
- Why does Inventory keep customer id and total in its own table? (It must be able to rebuild its answer without calling Orders.)
- Why one schema per service in a shared database, and what would change with one database per service?

### Payments service (S2-04), questions to be able to answer
- How is the emulator mode switched without a restart, and why a Redis key? (Read on every payment; the admin UI will write the same key. A Redis outage falls back to the configured default, because the mode is a demo convenience and must not stop payments.)
- Same handler shape as Inventory: commit the payment, then publish. Why can a customer not be charged twice? (Payment row keyed by order id; a redelivery repeats the stored answer.)
- What does the "timeout" mode model, and what does it NOT model? (It reports a failure after a delay. A bank that never answers needs a timeout in the caller, which stage 3 adds with the saga.)
- Why is the failed payment also stored?

### Lesson: sync disposal of an async resource (found while writing the Payments tests)
- Symptom: three tests took 25-45 seconds although every assertion ran in milliseconds. Timing laps showed the time was spent in `host.Dispose()`, after the test body.
- Cause: `IHost` only exposes the synchronous `Dispose`. The DI container then disposes async-only singletons (the RabbitMQ connection) by blocking a thread on `DisposeAsync`, which stalls until internal timeouts (~45 s). Disposing the publisher manually first made it vanish, which pinned it down.
- Fix: a small `TestHost` wrapper that disposes asynchronously (`await using`). Production workers are fine because `RunAsync` disposes the host asynchronously.
- Takeaway: sync-over-async disposal is a classic way to get mysterious shutdown delays (and deadlocks with a synchronization context). Measure with laps instead of guessing.

### Orders switch-over (S2-05/S2-06): what changed and what to say about it
- Checkout used to reserve stock in one database transaction (strong consistency inside one service). Now it saves a `Pending` order and publishes `order.created`; Inventory and Payments answer with events (eventual consistency across services). Say what you gained (independent services and data, a failure in Payments does not break ordering) and what you gave up (the order is not final when the HTTP call returns, extra failure modes, the gaps listed in ADR 0006).
- Stock no longer lives in the catalog. The catalog cache has no invalidation on checkout any more.
- Demonstrated in Docker: decline -> `Cancelled` with reason; 11 units of a 10-unit product -> `Cancelled`; Payments stopped -> order waits in `StockReserved`, the queue holds the message, after restart the order becomes `Confirmed`.
- Found while testing: until a consumer service has started once, its queue does not exist, so a published message is returned as unroutable (the order stayed `Pending`). Fix: the publishing service declares all queues at startup (`AddTopology`). Lesson: topology ownership and start order.
- Known limitation visible to users: when payment fails, the reserved stock is not released (gap 3, stage 3 compensation) and the cancellation reason for stock shows a product id.
- Questions: why choreography first; what happens if Orders dies right after the commit (Pending forever; outbox); how a late `payment.succeeded` for a cancelled order is handled today (logged as needing a refund); why event handlers are idempotent commands; what `mandatory` plus `AddTopology` protect against.

### End-to-end tests found a retry loop that the manual Docker check had hidden (S2-07)
- Symptom: the new end-to-end tests were very slow (12 competing buyers took 2 min 34 s instead of ~1 s) and one failed with an order stuck in `StockReserved`. In the Docker demo everything looked fine: orders ended `Confirmed`.
- Cause: after confirming an order, Orders publishes `order.confirmed` with the `mandatory` flag, but nobody consumes that event, so no queue is bound and the broker returns the message as unroutable. The publish threw AFTER the database commit, the consumer requeued the `payment.succeeded` message, redelivery found the order already confirmed, published again, failed again, forever (325 warnings in the API log, `orders.events` permanently holding unacknowledged messages). The consumer handles one message at a time and waits 2 s before each requeue, so every looping message stalled the others.
- Why the manual check missed it: the order status was already committed, so the UI looked right. The damage was in the logs and in latency. Lesson: after a manual check also read the logs and the queue depths, and prefer tests that assert on end state AND on time.
- Fix: a message type declares `RequiresConsumer`. Commands in disguise (`order.created`, `stock.reserved`, ...) must have a bound queue and fail loudly; pure announcements (`order.confirmed`) may have none. A contract test asserts the invariant: every message that requires a consumer is bound to some queue.
- This is gap 2 of ADR 0006 in action: an always-failing message blocks a queue. It is the best motivation for stage 3 (retry with a limit and a dead-letter queue instead of an endless requeue).
- Questions: when is `mandatory` right and when wrong; why requeue-forever is dangerous; what a dead-letter queue changes; how you would notice this in production (queue depth and unacked metrics, error rate, consumer lag).

### A shutdown race that only CI showed (fix/consumer-stop-race)
- Symptom: after merging S2-07 the first CI run on `main` was green, a rerun failed with all 39 tests passed but `Test Collection Cleanup Failure (api)`: a `NullReferenceException` while the test server shut down.
- Cause: `RabbitMqConsumer.StopAsync` checked `_channel is null`, then used and finally cleared the field. When two stops overlapped, the second one reached `_channel.DisposeAsync()` after the first had set the field to null. Deliveries that were already in flight when the consumer was cancelled had the same hazard (`_channel!` in the message path).
- Fix: `Interlocked.Exchange` takes ownership of the channel (a second stop becomes a no-op) and message processing uses the channel of the consumer that received the message. A test calls `StopAsync` on the same consumer 8 times concurrently (it failed about one run in three on the old code, passes 6 of 6 now).
- Lessons: "green once" proves little for lifecycle code; reruns of CI are a cheap flakiness detector; nullable state touched from several threads needs an atomic hand-over, not a check-then-act.
- Questions: what is check-then-act and how do you fix it (Interlocked, locks, ownership transfer); why must `StopAsync` be idempotent; what the broker does with unacknowledged messages when a channel closes.
