# ADR 0001: Clean Architecture with CQRS

- Status: accepted
- Date: 2026-10-07

## Context
The project must show clear boundaries and testability, and will grow from a monolith into several services (Orders, Inventory, Payments). We need a structure that makes that split cheap and keeps business rules independent of EF Core, RabbitMQ and ASP.NET.

## Options considered
1. **Single project, folders by feature.** Fastest to start. Boundaries are conventions only and erode; splitting later is expensive.
2. **Clean Architecture (Domain, Application, Infrastructure, Api) + CQRS with MediatR.** More files up front. Dependencies are enforced by project references; handlers are small and easy to test; commands and queries map naturally to messages later.
3. **Vertical slices without layers.** Very pragmatic, but less familiar to interviewers and harder to explain dependency rules with.

## Decision
Option 2. Dependencies point inward: Domain has none, Application depends on Domain, Infrastructure on Application and Contracts, Api composes everything. Writes go through commands, reads through queries. Message contracts live in a separate `OrderFlow.Contracts` project so services share them without sharing internals.

## Consequences
- More ceremony for trivial CRUD; accepted for the learning value.
- MediatR is a pipeline for validation and logging behaviours; check its license and version at the time stage 1 starts.
- Architecture tests can later enforce the dependency rule.

## Interview angle
"How is your solution structured and how do you keep business logic independent of infrastructure? How does CQRS look in your code?"
