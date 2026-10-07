# ADR 0002: Docker Compose with healthchecks for local environment

- Status: accepted
- Date: 2026-10-07

## Context
The system depends on PostgreSQL, RabbitMQ, Redis and Seq. Anyone (including CI and an interviewer) must be able to start it with one command, and services must not start before their dependencies are usable.

## Options considered
1. **Install dependencies locally.** Drifts between machines, hard to reset.
2. **docker compose with healthchecks and `depends_on: condition: service_healthy`.** Reproducible, disposable, close to how services run in production.
3. **.NET Aspire.** Good tooling, but hides orchestration details; revisit after the compose version is understood.

## Decision
Option 2. Image versions are pinned (no `latest`). Every infrastructure service has a healthcheck; `api` waits for all of them. Ports bind to `127.0.0.1` only. Credentials come from `.env` (git-ignored) with `.env.example` as the template. The API image is built with a multi-stage Dockerfile: restore layer cached by project files, publish in the SDK image, runtime on the ASP.NET Alpine image as a non-root user.

The API distinguishes liveness (`/health/live`, process is up) from readiness (`/health/ready`, dependencies reachable) so an orchestrator can restart a dead process without restarting it just because a dependency is down.

## Consequences
- Startup is slower because of healthcheck intervals.
- Seq runs without authentication in compose: local development only. Production needs credentials.
- Pinned versions need regular updates (Dependabot watches the Dockerfile; compose images are updated manually).

## Interview angle
"Why a healthcheck? What does `service_healthy` change compared with plain `depends_on`? Liveness vs readiness? What does a multi-stage Dockerfile buy you?"
