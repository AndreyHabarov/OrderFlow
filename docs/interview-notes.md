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
