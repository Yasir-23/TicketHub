# Software Requirements Specification (SRS)
## Project Name: TicketHub
**Version:** 1.0.0  
**Standard:** IEEE 830 Adapted  

---

## 1. Introduction
### 1.1 Purpose
This document establishes the functional, behavioral, and non-functional specifications for the TicketHub Web API.

### 1.2 Scope
TicketHub is a backend RESTful service built with ASP.NET Core (.NET 8) designed for event lifecycle management, high-concurrency ticket reservations, distributed hold expirations, and financial reporting.

---

## 2. System Actors & Role-Based Access Control (RBAC)

* **Admin:** Full administrative rights. Can create and modify venues, seating layouts, and events; configure section pricing; access executive financial reports.
* **Customer:** Authenticated public user. Can browse active events, hold up to 6 seats per event, execute payments, manage personal bookings, and request eligible cancellations.
* **Anonymous User:** Can view published events, search, filter, and view seating availability.

---

## 3. Strict Business Rules (BR)

* **BR-01 (Strict Mutual Exclusion):** A seat can only belong to one active `Held` or `Confirmed` record at any given time. Concurrent attempts on the same seat must allow exactly one request to succeed while immediately returning HTTP 409 Conflict to all others.
* **BR-02 (Anti-Scalping / Purchase Limit):** A single customer cannot hold or purchase more than 6 total seats for a single event across all their active bookings.
* **BR-03 (Temporal Validity):** Events with start dates in the past (`StartDateUtc <= UtcNow`) are read-only and cannot accept holds or bookings.
* **BR-04 (Hold Expiration):** A seat hold expires exactly 10 minutes (600 seconds) after creation. Expired holds become immediately available for reallocation by any user.
* **BR-05 (Cancellation Window):** Bookings can only be cancelled by customers if the request occurs at least 48 hours prior to the event's scheduled start time (`UtcNow <= Event.StartDateUtc - 48 hours`). Cancellations outside this window are rejected with HTTP 422 Unprocessable Entity.
* **BR-06 (Inventory Reversion):** When a booking is cancelled or a hold expires, the associated seats must immediately transition back to `Available` status.
* **BR-07 (Payment Idempotency):** All checkout requests must require an `Idempotency-Key` header. Duplicate submissions with identical keys must return the cached initial result without executing a second transaction or double-charging.
* **BR-08 (Soft Deletion):** Venues and Events cannot be physically deleted from the database if historical bookings reference them. They must use soft deletion (`IsDeleted = true`) with Global Query Filters.

---

## 4. Functional Requirements

### 4.1 Venue & Seating Hierarchy (FR-VEN)
* **FR-VEN-01:** The system shall allow Admins to create venues with physical metadata (Name, Address, City, Capacity).
* **FR-VEN-02:** The system shall allow Admins to configure Sections within a venue (e.g., VIP, Floor, Balcony).
* **FR-VEN-03:** The system shall allow Admins to generate Rows and individual Seats (Row Number, Seat Number) within sections.

### 4.2 Event Management (FR-EVT)
* **FR-EVT-01:** Admins shall create events linked to a specific venue, specifying Title, Description, Category (Concert, Sports, Seminar), StartDateUtc, and EndDateUtc.
* **FR-EVT-02:** Admins shall define ticket pricing per venue section for each specific event.
* **FR-EVT-03:** Customers and anonymous users shall search events by keyword and filter by City, Category, and Date Range.
* **FR-EVT-04:** Event listings must be paginated with configurable page size (default: 20, max: 100) and sortable by Date or Price.

### 4.3 High-Concurrency Seat Hold (FR-HLD)
* **FR-HLD-01:** The system shall expose an endpoint allowing authenticated customers to select and hold 1 to 6 seats for an event.
* **FR-HLD-02:** Seat holds must atomically set seat states to `Held`, associate the hold with the customer ID, and set `HoldExpiresAtUtc = UtcNow + 10 minutes`.
* **FR-HLD-03:** If any selected seat in a batch request is already held or confirmed, the entire transaction must roll back atomically, returning HTTP 409 Conflict.

### 4.4 Resilient Payment & Checkout (FR-PAY)
* **FR-PAY-01:** The system shall expose a checkout endpoint accepting `HoldId`, payment details, and an `Idempotency-Key` header.
* **FR-PAY-02:** Checkout shall verify that the hold has not expired before initiating payment.
* **FR-PAY-03:** The system shall integrate a simulated payment gateway featuring randomized responses (80% Success, 10% Failure, 10% Timeout/Network Latency).
* **FR-PAY-04:** Outbound calls to the payment gateway must be governed by Polly v8 policies (retries with exponential backoff and circuit breaker).
* **FR-PAY-05:** Upon payment success, the booking state transitions to `Confirmed`, seat records update to `Booked`, and a payment transaction record is committed.

### 4.5 Ticket Issuance & Self-Service Management (FR-BKG)
* **FR-BKG-01:** Upon confirmation, the system shall generate a cryptographically unique Ticket Number and QR code payload.
* **FR-BKG-02:** Customers shall retrieve their active and historical bookings.
* **FR-BKG-03:** Customers shall cancel bookings if BR-05 is satisfied, updating booking status to `Cancelled` and returning seats to `Available`.

### 4.6 Background Automation (FR-BKG-JOB)
* **FR-JOB-01:** An automated background worker shall execute every 15 seconds to identify and purge expired seat holds, reverting seat states.
* **FR-JOB-02:** An asynchronous message channel worker shall decouple ticket PDF/QR generation and email notification delivery from the checkout request path.
* **FR-JOB-03:** A scheduled worker shall run daily to notify customers 24 hours prior to their upcoming event.

### 4.7 Executive Reporting (FR-REP)
* **FR-REP-01:** Admins shall query gross revenue grouped by event.
* **FR-REP-02:** Admins shall query real-time occupancy distribution (Sold, Held, Available) per event and section.
* **FR-REP-03:** Admins shall query the Top 5 highest-grossing events of all time.
* **FR-REP-04:** Admins shall query daily sales volume filtered by custom start and end date ranges.

---

## 5. Non-Functional Requirements (NFR)

* **NFR-01 (Concurrency & Data Integrity):** Under simulated loads of 100 concurrent requests attempting to hold the same seat, zero duplicate bookings must occur. Exactly 1 request succeeds; 99 fail cleanly.
* **NFR-02 (Performance & Latency):** Public event catalog listings and seat availability queries must return in under 500ms under load, utilizing Redis distributed caching.
* **NFR-03 (Fault Tolerance):** Payment gateway timeouts or external disruptions must not leave database records in corrupt intermediate states.
* **NFR-04 (Security & Rate Limiting):** Authentication endpoints and booking endpoints must enforce rate limiting (e.g., max 10 booking attempts per minute per IP/user).
* **NFR-05 (Standardized API):** All endpoints must follow URL versioning (`/api/v1/...`) and adhere to RFC 7807 ProblemDetails specifications for errors.