# Engineering Handbook & AI Rules of Engagement (AGENTS.md)
## Project Name: TicketHub
Version: 1.0.0

---

## 1. Ground Rules for Coding Agents
1. Zero Warning Policy: All code must compile with 0 warnings and 0 errors under dotnet build.
2. 100% Test Pass Policy: Every existing and new unit/integration test must pass under dotnet test.
3. Conventional Commits Only: Format commits as:
   - feat: new feature
   - fix: bug fix
   - test: adding or updating tests
   - refactor: code improvements without behavior changes
   - docs: documentation updates
4. Leave Working Tree Uncommitted: Never execute git commit unless explicitly commanded by the user. Run git status at the end of each milestone.
5. Contract & Rule Adherence: Strictly follow docs/SRS.md, docs/SDD.md, and docs/API.md. Never invent endpoint paths or payload formats.

---

## 2. Solution Structure (Clean Architecture)
- TicketHub.sln
  - src/TicketHub.Domain (Entities, Value Objects, Enums, Domain Exceptions)
  - src/TicketHub.Application (Commands, Queries, DTOs, FluentValidation, MediatR Behaviors)
  - src/TicketHub.Infrastructure (EF Core DbContext, PostgreSQL configs, Redis, Background Workers)
  - src/TicketHub.Api (Controllers /api/v1/..., Middlewares, Rate Limiter, Swagger UI)
  - tests/TicketHub.UnitTests (Domain, Application, Command/Query handler tests)
  - tests/TicketHub.IntegrationTests (WebApplicationFactory, Database tests)

---

## 3. Milestone Delivery Roadmap

- Milestone 1: Solution Setup, Clean Architecture Skeleton, EF Core PostgreSQL base, Identity & JWT Auth.
- Milestone 2: Venues, Sections, Rows/Seats layout generation, Event catalog, and Section pricing.
- Milestone 3: High-concurrency Seat Hold engine (atomic update lock, 10-minute hold window, 409 conflict handling).
- Milestone 4: Resilient simulated payment gateway, Idempotency-Key validation, Polly v8 integration, and Booking confirmation.
- Milestone 5: Background processing workers (Expired hold reaper and asynchronous ticket QR channel worker).
- Milestone 6: Redis distributed caching, cache invalidation tokens, and ASP.NET Core rate limiting.
- Milestone 7: Executive reporting engine (Revenue, Occupancy, Top 5 events, Daily sales).
- Milestone 8: Docker multi-container stack, Health Checks (/health), and Integration Tests.