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
