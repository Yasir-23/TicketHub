# TicketHub

TicketHub is a .NET 8 REST API for event discovery, venue seating, short-lived seat holds, ticket checkout, and administrative sales reporting. Its booking flow uses PostgreSQL transactions and a filtered unique index to protect seat inventory under concurrent requests.

## Architecture

```text
Clients
  |
  v
TicketHub.Api            Controllers, JWT/RBAC, ProblemDetails, rate limits, health checks
  |
  v
TicketHub.Application    CQRS commands/queries, MediatR, validation, business workflows
  |
  v
TicketHub.Domain         Entities, rules, enums, domain exceptions
  ^
  |
TicketHub.Infrastructure EF Core/PostgreSQL, Redis, payment resilience, background workers
```

The solution follows Clean Architecture with CQRS through MediatR. Request handlers use the application abstractions; infrastructure supplies persistence, caching, payment simulation, and background processing.

## Technology

- .NET 8 / C# 12
- PostgreSQL 16 with EF Core and migrations
- Redis 7 distributed cache and generation-based catalog invalidation
- Docker Compose
- MediatR and FluentValidation
- Polly v8 HTTP resilience (retry, circuit breaker, and timeouts)
- Serilog structured request and application logging
- ASP.NET Core JWT bearer authentication, role authorization, and rate limiting

## Start locally

Docker Compose starts PostgreSQL, Redis, and the API:

```powershell
docker compose up -d --build
```

The API is published on port `5001`. The compose profile enables the Development environment so Swagger is available at [http://localhost:5001/swagger](http://localhost:5001/swagger).

Health endpoints:

- `http://localhost:5001/health` — liveness
- `http://localhost:5001/health/ready` — readiness, including configured PostgreSQL and Redis dependencies

The local Compose configuration seeds an admin account:

- Email: `admin@tickethub.com`
- Password: `AdminSecurePassword123!`

These are demo credentials from the API contract. Override `TICKETHUB_ADMIN_EMAIL` and `TICKETHUB_ADMIN_PASSWORD` for local customization; never use the demo password in a deployed environment.

## Business rules

- **BR-01 — Mutual exclusion:** A seat may have only one active hold or confirmed booking for an event. PostgreSQL's `UX_SeatHolds_Active` filtered unique index is the final concurrency guard.
- **BR-02 — Purchase limit:** A customer may hold or own at most six seats per event.
- **BR-03 — Event start:** Events at or before their start time cannot accept new holds or bookings.
- **BR-04 — Hold expiration:** Holds expire after ten minutes and expired inventory is eligible for reallocation.
- **BR-05 — Cancellation window:** Customer self-service cancellation is allowed up to (and including) 48 hours before event start.
- **BR-06 — Inventory reversion:** Cancelled bookings and expired holds release seats back to availability.

Checkout additionally requires an `Idempotency-Key`; duplicate keys do not initiate a second payment transaction.

## API

All application routes are versioned under `/api/v1`. See [docs/API.md](./docs/API.md) for request/response contracts and error statuses. Swagger is enabled for local Development runs.

## Tests

Run the complete solution test suite:

```powershell
dotnet test TicketHub.sln
```

The 50-request seat contention integration test starts a temporary PostgreSQL 16 container using Docker, migrates an isolated database, and exercises the real API pipeline. Docker must be running for this integration test.

Build the solution:

```powershell
dotnet build TicketHub.sln
```
