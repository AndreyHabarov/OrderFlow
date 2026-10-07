# OrderFlow: final plan

Revision of the original plan (Claude Docs artifact "План: платформа заказов"). Changes against the original are listed at the end.

## 1. Goal

A project for 10-12 evening weeks that proves middle+ level: reliable messaging, consistency without distributed transactions, operating services in containers. Success = a repo that starts with one command, ADRs for each decision, `docs/interview-notes.md` with problems, before/after metrics and rehearsed answers, and a configured Claude Code setup.

Roles: Claude writes code in small steps; the owner decides, reads the key 100-200 lines per stage, and breaks the system by hand.

## 2. Domain (fixed scope)

- Catalog: products with stock. Cart. Checkout creates an Order.
- Order lifecycle: `Pending -> StockReserved -> Paid -> Confirmed`, failure paths: `Cancelled` (stock unavailable), `PaymentFailed -> StockReleased -> Cancelled`.
- Payments emulator modes: success / bank decline / timeout. Nothing else.
- Roles: `Customer`, `Admin`. JWT access token + refresh token.

### Screens (the original referenced a sketch that does not exist; this replaces it)
1. Shop: product list, cart, checkout.
2. Order timeline: live status history of one order (SignalR).
3. Admin: queues and DLQ, manual retry, payment-mode switch, order search.

## 3. Architecture

Same stack as the original; clarified decisions:

- **Messaging library:** raw `RabbitMQ.Client` through stage 3 including hand-written outbox/inbox. Write an ADR comparing with MassTransit (check v9 licensing) and Wolverine at the end of stage 3 instead of rewriting. This removes the "rewrite in the middle of stage 3" risk.
- **Saga:** choreography first (stage 2), orchestrator in Orders (stage 3), ADR comparing them. Cut candidate: keep only one if time is short.
- **Redis, justified:** catalog cache-aside with invalidation; idempotency keys for `POST /orders`. Stock reservations live in PostgreSQL with `expires_at` and an expiry job (no Redis locks unless an ADR justifies them).
- **Contracts:** `OrderFlow.Contracts` project, message envelope (`MessageId`, `CorrelationId`, `CausationId`, `OccurredAt`, `Type`, `Version`), additive-only changes.
- **Data:** PostgreSQL, one schema per service, outbox + inbox tables per service.
- **Tests:** unit (domain, handlers), integration on Testcontainers (real PostgreSQL + RabbitMQ), one Playwright smoke test for the UI, k6 for load in stage 5.

## 4. Stages (realistic: 10-12 weeks)

Every stage ends with: working result, commits, ADR, code walkthrough, "break it yourself", 10-question interview, metrics in `interview-notes.md`.

### Stage 0. Foundation (3-4 evenings)
- Restructure into the target layout (see `CLAUDE.md`): `src/`, `tests/`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, analyzers. Move the template project to `src/OrderFlow.Api`, remove the weather sample.
- `docker-compose.yml`: PostgreSQL, RabbitMQ (management UI), Redis, Seq; pinned versions, healthchecks, `.env.example`.
- Health endpoints (`/health/live`, `/health/ready`), Serilog to Seq, `.gitignore` (add `.idea/`).
- GitHub Actions: build + test on PR. Dependabot. Branch protection on `main`.
- Claude Code setup: finalize `CLAUDE.md`, skills, subagents, hooks (section 6).
- `README.md` with a Mermaid architecture diagram, quick start and a 3-minute demo script (updated every stage). `docs/interview-notes.md` template: situation, cause, solution, numbers before/after, what I learned.
- Secrets via `dotnet user-secrets` and `.env`, never in `appsettings.*.json`.
- Rename the default branch `master` -> `main`; after CI exists, enable a GitHub ruleset on `main` (PR required + status checks).
- ADR 0001 (Clean Architecture + CQRS), ADR 0002 (compose + healthchecks).
- **Done when:** `docker compose up` brings everything up, healthchecks green, CI green on a PR.
- **Be able to answer:** why healthcheck and `depends_on: condition: service_healthy`; what a multi-stage Dockerfile does and why; why pin image versions.

### Stage 1. Monolith without a broker (1.5-2 weeks)
- Catalog, cart, checkout, JWT + roles, EF Core migrations, Redis cache for catalog, idempotent `POST /orders`, minimal React (screen 1), CORS, validation, ProblemDetails.
- Integration tests on Testcontainers in CI.
- **Done when:** an order is created from the UI; integration tests pass in CI; migration applies on a Docker DB without data loss.
- **Be able to answer:** how CQRS looks in this code; cache invalidation strategy; why `Idempotency-Key`; EF Core N+1 and how you avoided it.

### Stage 2. Services and RabbitMQ (2 weeks)
- Extract Inventory and Payments (workers). Topology: exchanges, queues, routing keys documented in `docs/architecture.md`.
- Chain: `OrderCreated -> StockReserved -> PaymentSucceeded -> OrderConfirmed` (choreography).
- Manual ack, prefetch, durable queues, persistent messages, publisher confirms, graceful shutdown of consumers.
- **Done when:** order passes the chain; stopping Payments accumulates messages that are processed after restart.
- **Be able to answer:** at-least-once vs exactly-once; ack/nack/prefetch; publisher confirms; why `MessageId`; what happens to unacked messages when a consumer dies.

### Stage 3. Reliability: outbox, inbox, retry, DLQ, saga (2-2.5 weeks)
- Outbox (same transaction as data change) + background publisher; inbox deduplication by `MessageId`.
- Retry via TTL queues, DLQ, poison message handling, compensation (`ReleaseStock`), orchestrator in Orders + comparison ADR, reservation expiry job.
- Messaging library ADR (raw client vs MassTransit vs Wolverine).
- Automated scenarios: bank decline, duplicate delivery, broker down between DB commit and publish, consumer crash mid-processing, out-of-order events.
- **Done when:** all scenarios pass as tests and data stays consistent.
- **Be able to answer:** broker dies after DB commit; choreography vs orchestration; how to make a handler idempotent; why not 2PC; outbox ordering guarantees and polling vs CDC.

### Stage 4. Product: SignalR and admin (1.5-2 weeks)
- Realtime order timeline, payment-mode switch, admin pages (queues, DLQ view, manual retry), role-based authorization, screens 2 and 3, one Playwright smoke test.
- **Done when:** any failure can be triggered from the UI and the whole order path is visible.
- **Be able to answer:** SignalR scaling (backplane, sticky sessions); how admin is protected; why not polling.

### Stage 5a. Observability and CI/CD (1.5 weeks)
- OpenTelemetry traces Api -> RabbitMQ -> Worker (context propagation in message headers), metrics (queue depth, handler latency, outbox lag), structured logs with `CorrelationId`.
- GitHub Actions: build, test, build and push images to GHCR.
- **Done when:** by trace id you find the cause of an injected failure in minutes.
- **Be able to answer:** logs vs metrics vs traces; how trace context crosses a broker; what you alert on.

### Stage 5b. Deploy and performance (1 week, can be cut)
- VPS deploy on push to `main`, PostgreSQL backups with a tested restore, k6 load test, one query optimized with `EXPLAIN ANALYZE` before/after numbers.
- **Done when:** deploy by push; restore from backup rehearsed; numbers recorded.
- **Be able to answer:** how to roll back a release; how to restore from backup; what the bottleneck was and how you proved it.

Bonus: k3s instead of compose, Nginx/Caddy with TLS.

## 5. Scope cut order (when behind schedule)
1. Bonus (k3s, TLS)
2. Stage 5b
3. Orchestrator or choreography (keep one, keep the ADR)
4. Admin extras (search, retry UI) but keep payment-mode switch and DLQ view
5. Playwright smoke test

Never cut: outbox, inbox, DLQ, compensation, ADRs, interview notes.

## 6. Claude Code setup

| What | Where | Content |
|---|---|---|
| Memory | `CLAUDE.md` | created |
| Skills | `.claude/skills/` | `add-endpoint`, `add-consumer` (inbox, retry, DLQ), `add-migration`, `adr`, `review`, `stage-wrap-up` (walkthrough + interview + metrics) |
| Subagents | `.claude/agents/` | `code-reviewer`, `test-writer`, `interviewer` (middle+ questions, scores answers) |
| Hooks | `.claude/settings.json` | `dotnet format` after edits; `dotnet test` before commit; block edits to committed migrations; block `.env` edits |
| MCP | project settings | GitHub, PostgreSQL (read-only), Playwright |

Create skills and hooks after the stage 0 plan is confirmed. Verify file formats against current Claude Code docs.

### Tooling roadmap (add each tool only when a real need appears)
- **Stage 0:** `CLAUDE.md`, Plan mode, permissions allowlist/denylist, 2-3 skills, 2 subagents (`code-reviewer`, `interviewer`), 2-3 hooks, GitHub MCP.
- **Stages 1-3:** turn any task repeated three times into a skill; add `security-reviewer` subagent; use `/clear` between unrelated tasks and git worktrees for parallel backend/frontend sessions; spec first, then plan, for each feature.
- **Stage 5a:** Claude Code in GitHub Actions for PR review, CodeQL, gitleaks, Trivy image scan, Dependabot, Conventional Commits check.
- **Dev tools:** Scalar (OpenAPI UI), Bruno, DBeaver, Respawn, Bogus, Verify, Shouldly or AwesomeAssertions (check FluentAssertions licensing), Microsoft.Extensions.Resilience (Polly).
- **Bonus:** .NET Aspire, Prometheus + Grafana + Jaeger/Tempo, stage 6 "AI assistant in admin" via Claude API (tool use, structured output, evals, restricted data access).
- **What employers look for in AI-assisted developers:** reviewing AI code, context/spec writing, automation around AI (hooks, CI review), security awareness (secrets, prompt injection, agent permissions).

## 7. Workflow rules
- Plan mode for each feature; small commits; tests are the readiness criterion.
- Branches `feature/<stage>-<name>`, PR per feature or stage into `main`, squash or rebase merge. Conventional Commits.
- After each stage: ADR -> walkthrough -> break it -> interview -> metrics.
- English for README, ADR, commits. Knowledge lives in the repo, not in chat history.

## 8. Risks
- Microservices/K8s too early. Code you cannot explain. Stage overrun (cut per section 5). Emulator over-engineering. Secrets in git. Library/image versions and licenses drifting.
- New: timeline optimism (plan 12 weeks, not 8-10); half-finished outbox when switching libraries (fixed by deciding in stage 2); undefined UI scope (fixed by section 2 screens).

## 9. Changes against the original plan
1. Timeline 8-10 -> 10-12 weeks; stage 5 split into 5a (must) and 5b (cuttable).
2. Missing sketch replaced by a concrete screen list.
3. Explicit scope cut order.
4. Messaging library decided up front (raw client + comparison ADR), no mid-stage rewrite.
5. Redis scope narrowed to cache + idempotency; reservation TTL moved to PostgreSQL.
6. Added: message envelope and contract versioning, roles/refresh tokens, publisher confirms, out-of-order and consumer-crash tests, health endpoints, CI branch protection, Dependabot, central package management, Playwright smoke, pinned image versions.
7. Stage 0 now accounts for the existing template project in the repo.
