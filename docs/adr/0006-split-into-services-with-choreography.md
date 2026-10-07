# ADR 0006: Split Inventory and Payments into services, connected by choreographed events

- Status: accepted
- Date: 2026-10-07

## Context
Stage 1 is a monolith: checkout reserves stock in the same database transaction that creates the order. Stage 2 introduces RabbitMQ. We need to decide who owns what, how services talk, and what guarantees we claim before the reliability patterns (outbox, inbox, DLQ) arrive in stage 3.

## Decisions

### 1. Services and data ownership
- **Orders** (the existing API): catalog (name, price), carts, orders, authentication.
- **Inventory** (new worker): owns stock quantities and reservations, schema `inventory`.
- **Payments** (new worker): owns the payment emulator and payment records, schema `payments`.
- One PostgreSQL database, one schema and one `DbContext` per service; services never read each other's tables.
- **Stock leaves Orders.** `Product.StockQuantity` is removed from the catalog. The catalog no longer shows availability; an unavailable item is discovered when Inventory rejects the reservation and the order is cancelled with a reason. (A read model fed by events would restore availability in the UI; that is a candidate for stage 4, not needed to learn the messaging patterns.)

### 2. Choreography, not orchestration
Each service reacts to events and emits new ones; nobody drives the whole flow:

`OrderCreated` (Orders) -> `StockReserved` | `StockReservationFailed` (Inventory) -> `PaymentSucceeded` | `PaymentFailed` (Payments) -> Orders updates the order and emits `OrderConfirmed`.

An orchestrator inside Orders is introduced in stage 3 and compared with this design in a separate ADR.

### 3. Topology
- One durable topic exchange `orderflow.events`; the routing key is the message type (`order.created`, `stock.reserved`, ...), declared with `[MessageType]` on each contract record.
- One durable queue per consuming service (`orders.events`, `inventory.events`, `payments.events`) bound to the routing keys it cares about. Publishers do not know their consumers.
- Messages are persistent, queues durable, publishing uses **publisher confirms**, consuming uses **manual ack** with an explicit **prefetch**, and consumers shut down gracefully (stop consuming, finish in-flight messages).

### 4. Envelope in AMQP properties
`MessageId`, `CorrelationId`, `Type`, `Timestamp` and `ContentType` use the native AMQP properties; `causation-id` and `version` are headers; the body is the JSON payload. This keeps payloads clean and lets any tool inspect the metadata.

### 5. Guarantees claimed in stage 2 (and the known gaps)
- **At-least-once delivery.** A message may arrive twice. Consumers are made safe by natural keys: reservations and payments are keyed by order id, and order status transitions ignore repeats. A duplicate re-publishes the same outcome, so a lost publish can be recovered by redelivery.
- **Known gap 1:** Orders publishes `OrderCreated` after its database commit, directly. If the process dies in between, the order stays `Pending` forever. Stage 3 fixes this with the transactional outbox.
- **Known gap 2:** an exception in a handler requeues the message, so a poison message loops. Stage 3 adds retry with TTL and a dead-letter queue.
- **Known gap 3:** when payment fails the stock stays reserved. Stage 3 adds the `ReleaseStock` compensation.
- **Known gap 4:** no timeout for a payment that never answers. Stage 3.

## Consequences
- Eventual consistency: right after checkout the order is `Pending`; the UI polls until SignalR arrives in stage 4.
- Each gap above is a deliberate, documented stepping stone and an interview talking point ("what does your system guarantee, and what breaks it?").
- More moving parts locally: two extra containers and three queues to observe in the RabbitMQ UI.

## Interview angle
"At-least-once vs exactly-once? Why a topic exchange and a queue per service? What is a publisher confirm and what does it not guarantee? Prefetch? What happens if a service dies after commit but before publish? Choreography vs orchestration trade-offs?"
