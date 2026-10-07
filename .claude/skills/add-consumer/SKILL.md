---
name: add-consumer
description: Add a RabbitMQ consumer with inbox deduplication, retry via TTL and dead-lettering. Use from stage 2 onward whenever a service must handle a new message type. Draft: refine it after the first real consumer is written.
---

# Add a consumer

Preconditions: message contract exists in `OrderFlow.Contracts` and carries the standard envelope (`MessageId`, `CorrelationId`, `CausationId`, `OccurredAt`, `Type`, `Version`). If not, add it first.

Checklist (every item must be covered by code or a test):

1. **Topology:** durable queue bound to the right exchange and routing key; retry queue with TTL that dead-letters back to the main queue; final dead-letter queue. Document it in `docs/architecture.md`.
2. **Handler:** a MediatR command or a dedicated handler; no business logic in the consumer class itself.
3. **Inbox:** in one DB transaction insert the `MessageId` into the inbox table (unique) and apply the side effects. A duplicate `MessageId` is acknowledged without doing the work again.
4. **Ack policy:** manual ack after the transaction commits. Transient failure: publish to the retry queue with an incremented attempt header, then ack. Attempts exhausted or poison message: publish to the DLQ, then ack. Never `nack` with requeue in a loop.
5. **Prefetch** is set explicitly and justified.
6. **Logging and tracing:** push `CorrelationId` into the log context; propagate trace context from message headers.
7. **Graceful shutdown:** stop consuming, finish in-flight messages, then close the channel.
8. **Tests (Testcontainers, real RabbitMQ and PostgreSQL):** happy path, duplicate delivery, handler throws once then succeeds (retry), always throws (lands in DLQ), consumer killed before ack (message redelivered).
9. Add an ADR if a new trade-off was made, and questions to `docs/interview-notes.md`.
