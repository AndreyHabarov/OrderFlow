# OrderFlow

Learning project: an order-processing platform with a saga, built to prepare the owner for middle+ .NET interviews.
The goal is NOT volume of code. The goal is that the owner can explain and defend every decision:
message delivery reliability, consistency without distributed transactions, running things in containers.

Full plan: `docs/PLAN.md`. Architecture decisions: `docs/adr/`. Interview prep log: `docs/interview-notes.md`.

## Working agreement (read first)

- Talk to the owner in **Russian**. Write code, comments, README, ADR, commit messages and PR text in **English**.
- The owner is a .NET developer (3.5 years; MS SQL, PostgreSQL, CQRS, background services). Do not over-explain basics; do explain messaging, saga, outbox, Docker, observability in depth.
- **Plan first.** For every feature: show a short plan + file list and wait for confirmation before writing code (use Plan mode).
- **Small steps.** One feature = one branch = several small commits. Tests are the definition of done.
- **Do not leave code the owner cannot explain.** After each stage: write ADR(s), walk through the 100-200 most important lines, then act as interviewer (10 middle+ questions), and record weak spots in `docs/interview-notes.md`.
- **Git:** do NOT add `Co-Authored-By` or any AI attribution lines to commits or PR text (owner decision). Never commit or push without the owner's confirmation unless they said "auto-commit" in this session. Never force-push, never rewrite pushed history, never push to `main` directly once PR workflow starts.
- If a stage runs over 2 weeks, propose cutting scope (cut order is in `docs/PLAN.md`) instead of extending.
- Library versions and licenses (MassTransit, Docker images) must be checked at the moment of use; do not trust memory.

## Stack

| Area | Choice |
|---|---|
| Runtime | .NET 10 (C#, nullable enabled), ASP.NET Core, Worker Services |
| Architecture | Clean Architecture, CQRS with MediatR, EF Core |
| DB | PostgreSQL, one schema per service |
| Broker | RabbitMQ via `RabbitMQ.Client` directly (no MassTransit in stages 2-3, see ADR) |
| Cache | Redis (StackExchange.Redis): catalog cache-aside with versioned namespaces |
| Realtime | SignalR (Redis backplane discussed in ADR) |
| Frontend | React + Vite + TypeScript, SignalR client |
| Observability | Serilog -> Seq, OpenTelemetry (traces + metrics) via OTLP |
| Tests | xUnit, Testcontainers (real PostgreSQL + RabbitMQ), Playwright smoke for UI |
| Infra | Docker, docker compose, GitHub Actions |

Payments is an **emulator** with 3 modes only: success, bank decline, timeout. Do not add more.

## Target repository layout

```
src/
  OrderFlow.Api/            ASP.NET Core host (orders, catalog, cart, auth, saga orchestration)
  OrderFlow.Application/    commands, queries, handlers, validators, abstractions
  OrderFlow.Domain/         entities, value objects, domain events (no dependencies)
  OrderFlow.Infrastructure/ EF Core, Redis, RabbitMQ, outbox/inbox
  OrderFlow.Contracts/      message contracts, topology, demo catalog (shared)
  OrderFlow.Messaging/      RabbitMQ publisher/consumer library (shared)
  OrderFlow.Inventory/      worker: stock and reservations (own schema)
  OrderFlow.Payments/       worker: payment emulator (own schema)
web/                        React app
tests/
  OrderFlow.UnitTests/
  OrderFlow.IntegrationTests/   Testcontainers
docs/
  PLAN.md  adr/  interview-notes.md  architecture.md
docker-compose.yml  Directory.Build.props  Directory.Packages.props
```

Current state: stage 0 in progress; the default template project already lives in `src/OrderFlow.Api` (weather sample still to be removed)
Update this section as the structure changes.

## Commands (fill in / keep accurate as they appear)

```bash
dotnet build OrderFlow.sln
dotnet test OrderFlow.sln
dotnet format OrderFlow.sln
docker compose up -d          # PostgreSQL, RabbitMQ (UI :15672), Redis, Seq
docker compose down -v        # destroys volumes: ask the owner first
cd web && npm run dev
```

## Conventions

- Central package management (`Directory.Packages.props`); warnings as errors in CI; `.editorconfig` is the source of style truth.
- Naming: commands `VerbNounCommand`, queries `GetNounQuery`, events in past tense (`OrderCreated`), handlers `<Message>Handler`.
- Every message has an envelope: `MessageId`, `CorrelationId`, `CausationId`, `OccurredAt`, `Type`, `Version`. Contracts live only in `OrderFlow.Contracts`; changes are additive (new fields optional) or a new version.
- Every consumer is idempotent: inbox table keyed by `MessageId`, processed in the same DB transaction as its side effects.
- Every state change that publishes an event goes through the **outbox** (same transaction). Never publish to the broker directly from a handler.
- Failure handling: retry with TTL queues -> dead-letter queue; nack without requeue loops is forbidden.
- Time via `TimeProvider`, no `DateTime.Now` in domain/application code.
- API: use classic MVC controllers (`[ApiController]`, classes in `Controllers/` inheriting `ControllerBase`), NOT minimal APIs. Controllers stay thin: map the request to a MediatR command or query and return the result. Health checks are the only endpoints mapped without a controller.
- API: validation at the boundary, ProblemDetails for errors, `Idempotency-Key` header required on order creation (unique key in PostgreSQL, see ADR 0004).
- EF Core migrations are generated by `dotnet ef`, never edited by hand once committed.

## Do not

- Do not commit secrets. Secrets only in `.env` (git-ignored) and user-secrets. This is a personal learning project, so `.env` holds local-only passwords and the assistant may read and edit it; never put real credentials of other services there.
- Do not use `latest` image tags in compose; pin versions.
- Do not start with microservices/Kubernetes; do not skip a stage's "done when" criteria.
- Do not mock the database or broker in integration tests; use Testcontainers.
- Do not edit committed migration files, ADRs of past stages (supersede them with a new ADR instead), or `docs/interview-notes.md` entries written by the owner.
- Do not run destructive commands (`docker compose down -v`, `git reset --hard`, dropping databases) without asking.

## Definition of done (per feature)

1. Builds with no warnings; `dotnet test` green (unit + Testcontainers integration).
2. Failure scenarios covered by a test where relevant (redelivery, broker down, bank decline).
3. Logs carry `CorrelationId`; new messages appear in traces.
4. ADR written if a non-trivial decision was made. README/`docs/architecture.md` updated if structure changed.
5. Small, conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`), feature branch, PR description with "what / why / how tested".

## Stage workflow

Stages 0-5 are defined in `docs/PLAN.md` with "done when" and "be able to answer" lists. At the end of each stage:
ADR -> code walkthrough -> owner breaks the system by hand -> interview round -> metrics before/after in `docs/interview-notes.md`.

## Claude Code setup in this repo

- `.claude/settings.json`: permissions allowlist/denylist and hooks (committed). `settings.local.json` is personal and git-ignored.
- Hooks (`.claude/hooks/`, Node scripts): block edits to `Migrations/`; format changed `.cs` files; run `dotnet test` before `git commit` when C#/project files are staged.
- Skills (`.claude/skills/`): `adr`, `add-consumer` (draft until stage 2). Add a skill when a task has been repeated three times.
- Subagents (`.claude/agents/`): `code-reviewer` (read-only review of the diff), `interviewer` (end-of-stage interview, updates `docs/interview-notes.md`).
- GitHub access goes through the `gh` CLI; a GitHub MCP server is not configured.
