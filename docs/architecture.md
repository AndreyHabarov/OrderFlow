# Architecture

Living document, updated at the end of each stage. Rationale for individual decisions is in `docs/adr/`.

## Solution layers (Clean Architecture)

| Project | Responsibility | Depends on |
|---|---|---|
| `OrderFlow.Domain` | entities, value objects, domain events | nothing |
| `OrderFlow.Application` | commands, queries, handlers, validators, abstractions (CQRS with MediatR) | Domain |
| `OrderFlow.Infrastructure` | EF Core, Redis, RabbitMQ, outbox/inbox, health checks | Application, Contracts |
| `OrderFlow.Contracts` | message contracts shared between services | nothing |
| `OrderFlow.Api` | HTTP host, auth, SignalR, composition root | Application, Infrastructure, Contracts |

Rule: dependencies point inward. Domain and Application never reference Infrastructure.

## Runtime topology (stage 0)

- `api` (ASP.NET Core) with `/health/live` (process is up) and `/health/ready` (PostgreSQL, Redis, RabbitMQ reachable).
- `postgres`, `rabbitmq`, `redis`, `seq`, each with a healthcheck. `api` starts only when all are healthy (`depends_on: condition: service_healthy`).
- Logs: Serilog to console and Seq; every request carries `CorrelationId` (`X-Correlation-Id` header).

## Messaging (to be defined in stage 2)

Every message carries an envelope: `MessageId`, `CorrelationId`, `CausationId`, `OccurredAt`, `Type`, `Version`.
Exchanges, queues and routing keys will be documented here when the broker is introduced.

## Data

PostgreSQL, one schema per service. Outbox and inbox tables per service (stage 3).

## Failure handling (to be defined in stage 3)

Retry via TTL queues, then dead-letter queue; outbox for atomic state change + publish; inbox for idempotent consumers; compensations in the saga.
