# Backlog

Source of truth for tasks. Plan and rationale: `docs/PLAN.md`. Take the first unchecked task, do it as one small commit, tick it in the same commit.
Task ID = `S<stage>-<nn>`. Stage details are expanded just before the stage starts.

## Stage 0. Foundation

- [x] S0-01 Repository, `.gitignore`, `CLAUDE.md`, `docs/PLAN.md`
- [x] S0-02 Move API to `src/`, add `tests/`
- [x] S0-03 Build props, central package management, `.editorconfig`, `.gitattributes`
- [x] S0-04 Clean Architecture projects + test projects
- [x] S0-05 `docker-compose.yml`: PostgreSQL, RabbitMQ (UI), Redis, Seq; pinned versions, healthchecks  (verified: all services healthy)
- [x] S0-06 Multi-stage `Dockerfile` for Api, `.dockerignore`, `api` service in compose (verified: image builds, api healthy)
- [x] S0-07 Health endpoints `/health/live` and `/health/ready` (PostgreSQL, Redis, RabbitMQ checks) (ready returns 503 until dependencies are up; checked without Docker)
- [x] S0-08 Serilog to Seq, `dotnet user-secrets` setup (verified: logs with CorrelationId arrive in Seq)
- [x] S0-09 GitHub Actions: build + test on PR; Dependabot (first run result to be verified on GitHub)
- [x] S0-10 Enable `main` ruleset (owner), switch to branch + PR workflow (repo made public; ruleset active: PR + build-test required)
- [x] S0-11 `README.md` with Mermaid diagram, quick start, demo script outline; `docs/architecture.md`
- [x] S0-12 ADR 0001 (Clean Architecture + CQRS), ADR 0002 (compose + healthchecks); `docs/interview-notes.md` template
- [x] S0-13 Claude Code: skills `adr`, `add-consumer`; subagents `code-reviewer`, `interviewer`; hooks; GitHub MCP (GitHub MCP skipped: gh CLI covers it; add-consumer is a draft until stage 2)
- [ ] S0-14 Stage wrap-up: walkthrough, break-it-yourself, interview round

## Stage 1. Monolith without a broker

- [x] S1-01 Domain model: Product, Cart, Order
- [x] S1-02 EF Core + PostgreSQL, first migration (migration applied to real PostgreSQL via migrate-on-startup)
- [x] S1-03 Catalog queries + Redis cache-aside (ProductsController, versioned cache namespaces, demo seed, ADR 0003)
- [x] S1-04 Cart and checkout commands (MediatR, validation) and their controllers (cart, checkout, order queries, temporary X-Customer-Id identity; concurrency test pending in S1-08)
- [x] S1-05 `Idempotency-Key` on `POST /orders` (unique key in PostgreSQL, ADR 0004; verified with 8 parallel requests)
- [x] S1-06 JWT auth, roles (Customer/Admin), refresh tokens (ADR 0005; refresh rotation with reuse detection verified live)
- [x] S1-07 ProblemDetails, global error handling, CORS (CORS policy from Cors:AllowedOrigins, verified with preflight)
- [ ] S1-08 Integration tests on Testcontainers; run in CI
- [ ] S1-09 React app: shop, cart, checkout (screen 1)
- [ ] S1-10 Stage wrap-up

## Stage 2. Services and RabbitMQ

- [ ] S2-01 Contracts + message envelope, topology doc
- [ ] S2-02 RabbitMQ publisher with confirms
- [ ] S2-03 Consumer base: manual ack, prefetch, graceful shutdown
- [ ] S2-04 Inventory worker (reserve stock)
- [ ] S2-05 Payments worker + emulator modes
- [ ] S2-06 Choreography chain `OrderCreated -> StockReserved -> PaymentSucceeded -> OrderConfirmed`
- [ ] S2-07 Test: Payments stopped, messages accumulate and are processed later
- [ ] S2-08 Stage wrap-up

## Stage 3. Reliability

- [ ] S3-01 Outbox (table, same transaction, background publisher)
- [ ] S3-02 Inbox deduplication by `MessageId`
- [ ] S3-03 Retry via TTL queues, DLQ, poison messages
- [ ] S3-04 Compensation `ReleaseStock`, reservation expiry job
- [ ] S3-05 Orchestrator in Orders + comparison ADR
- [ ] S3-06 Failure tests: bank decline, duplicate delivery, broker down, consumer crash, out-of-order
- [ ] S3-07 ADR: raw client vs MassTransit vs Wolverine
- [ ] S3-08 Stage wrap-up

## Stage 4. Product: SignalR and admin

- [ ] S4-01 SignalR hub + order timeline
- [ ] S4-02 Payment-mode switch API + UI
- [ ] S4-03 Admin: queues, DLQ view, manual retry, role-based authorization
- [ ] S4-04 Playwright smoke test
- [ ] S4-05 Stage wrap-up

## Stage 5a. Observability and CI/CD

- [ ] S5A-01 OpenTelemetry traces, context propagation through message headers
- [ ] S5A-02 Metrics: queue depth, handler latency, outbox lag
- [ ] S5A-03 CI: build and push images to GHCR
- [ ] S5A-04 CodeQL, gitleaks, Trivy; Claude Code PR review in Actions
- [ ] S5A-05 Stage wrap-up

## Stage 5b. Deploy and performance (cuttable)

- [ ] S5B-01 VPS deploy on push to `main`
- [ ] S5B-02 PostgreSQL backups + tested restore
- [ ] S5B-03 k6 load test, `EXPLAIN ANALYZE` before/after
- [ ] S5B-04 Stage wrap-up

## Ideas for later

- .NET Aspire, Prometheus + Grafana + Jaeger/Tempo, k3s, Nginx/Caddy with TLS
- Stage 6: AI assistant in admin via Claude API (tool use, structured output, evals)
