# Software Design Document (SDD)
## Project Name: TicketHub
Version: 1.0.0
Target Framework: .NET 8 (C# 12)
Database: PostgreSQL 16
Distributed Cache: Redis 7

---

## 1. Architectural Style & Clean Architecture Layers

TicketHub implements Clean Architecture combined with CQRS powered by MediatR.

Layer Responsibilities:
- TicketHub.Domain: Pure business logic, core entities, enums, domain exceptions. Zero external dependencies.
- TicketHub.Application: CQRS orchestration with MediatR. Commands, Queries, DTOs, FluentValidation validators, and pipeline behaviors.
- TicketHub.Infrastructure: Implementation of data persistence (EF Core DbContext, PostgreSQL), Redis distributed caching, simulated payment gateway with Polly v8, and background workers.
- TicketHub.Api: RESTful HTTP API surface, routing (/api/v1/...), JWT token handling, ASP.NET Core rate limiting, ProblemDetails middleware, and Swagger.

---

## 2. Cross-Cutting Concerns: MediatR Pipeline Behaviors

All requests dispatched through MediatR pass through a deterministic pipeline:
1. LoggingBehavior: Captures incoming command/query payload, tracks execution duration, and logs structured diagnostics.
2. ValidationBehavior: Inspects registered FluentValidation validators. Throws ValidationException (mapped to HTTP 400 ProblemDetails) before command execution.
3. TransactionBehavior: Wraps transactional commands inside an atomic database transaction.

---

## 3. Database Schema & Domain Model

Entities & Relationships:
- Venues: Id (PK, UUID), Name, City, Address, TotalCapacity, IsDeleted
- Sections: Id (PK, UUID), VenueId (FK), Name (VIP, Floor, Balcony), Capacity, IsDeleted
- Seats: Id (PK, UUID), SectionId (FK), RowNumber (e.g. A), SeatNumber (e.g. 12), RowVersion (xmin)
- Events: Id (PK, UUID), VenueId (FK), Title, Category, Description, StartDateUtc, EndDateUtc, IsDeleted
- EventSectionPricings: Id (PK, UUID), EventId (FK), SectionId (FK), PriceCents (BigInt)
- SeatHolds: Id (PK, UUID), EventId (FK), SeatId (FK), CustomerId (FK), HoldExpiresAtUtc, Status (Active, Released, Converted, Expired)
- Bookings: Id (PK, UUID), EventId (FK), CustomerId (FK), TotalAmountCents, Status (Pending, Confirmed, Cancelled), TicketNumber, QrCodePayload, CreatedAtUtc, CancelledAtUtc
- BookingItems: Id (PK, UUID), BookingId (FK), SeatId (FK), UnitPriceCents
- PaymentTransactions: Id (PK, UUID), BookingId (FK), IdempotencyKey, AmountCents, Status (Success, Failed, TimedOut), ProviderReference, CreatedAtUtc

Domain Enums:
- SeatStatus: Available = 0, Held = 1, Booked = 2
- HoldStatus: Active = 0, Released = 1, ConvertedToBooking = 2, Expired = 3
- BookingStatus: PendingPayment = 0, Confirmed = 1, Cancelled = 2
- PaymentStatus: Initiated = 0, Success = 1, Failed = 2, TimedOut = 3

---

## 4. High-Concurrency Seat Hold Engine (The Hot Path)

To satisfy BR-01 (Zero double-booking) under heavy concurrent traffic:

Hold Step-by-Step Logic:
1. Incoming HoldSeatsCommand specifies EventId, CustomerId, and requested SeatIds (Max 6 seats per BR-02).
2. Validate customer purchase count: CurrentActiveTickets + RequestedSeats <= 6. If exceeded, return HTTP 422 Unprocessable Entity.
3. Open an explicit read-committed transaction with PostgreSQL.
4. Execute atomic state check:
   UPDATE Seats SET Status = 1 WHERE Id = ANY(@SeatIds) AND Status = 0;
5. Verify affected rows count:
   - If affected_rows == requested_count: All seats secured. Insert into SeatHolds with HoldExpiresAtUtc = DateTime.UtcNow + 10 minutes and commit transaction.
   - If affected_rows < requested_count: One or more seats were claimed by a rival transaction. Roll back transaction immediately and return HTTP 409 Conflict.
6. Safety Net Constraint:
   A partial unique index enforces that no seat has more than one active hold at any time:
   CREATE UNIQUE INDEX UX_SeatHolds_Active ON SeatHolds (EventId, SeatId) WHERE Status = 0;

---

## 5. Resilient Payment Pipeline (Polly v8 & Idempotency)

5.1 Idempotency Key Handling:
- Every POST /api/v1/bookings/checkout requires header Idempotency-Key: <UUID>.
- The key is checked against PaymentTransactions.
- If currently processing: return HTTP 409 Conflict.
- If already completed: return the cached response payload immediately.

5.2 Polly Resilience Strategy:
Configured via Microsoft.Extensions.Http.Resilience:
- Retry Policy: 3 attempts with exponential backoff and jitter for transient errors (408, 500, 502, 503, 504).
- Circuit Breaker: Opens circuit for 15 seconds if 50% of requests fail across a 30-second window (minimum throughput of 10 requests).
- Timeout: Individual attempt timeout of 5 seconds; total execution timeout of 15 seconds.

---

## 6. Background Workers (Asynchronous Decoupling)

6.1 ExpiredHoldReaperWorker (BackgroundService):
- Executes every 15 seconds.
- Queries expired holds using FOR UPDATE SKIP LOCKED.
- Marks SeatHolds.Status = Expired and reverts associated Seats.Status = Available.

6.2 TicketFulfillmentChannelWorker (System.Threading.Channels):
- Uses bounded channel capacity of 5,000 items.
- When payment succeeds, writes to the channel and returns HTTP 200 to customer immediately.
- Worker reads from channel, generates cryptographic QR code, formats email ticket pass, and marks delivery.

---

## 7. Caching & Performance Architecture

- Caching Layers:
  1. IMemoryCache: Static lookup tables (Venues, Sections).
  2. IDistributedCache (Redis): Catalog queries (GET /api/v1/events) and seat availability maps.
- Cache Invalidation:
  Uses tokenized generation keys: events:catalog:{generation}.
  Admin event creation, updates, or cancellations increment the generation counter, invalidating cached catalogs instantaneously.