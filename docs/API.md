# API Specification & Contracts (API.md)
## Project Name: TicketHub
Base Path: /api/v1
Protocol: HTTPS
Standard: RESTful with RFC 7807 ProblemDetails for errors

---

## 1. Authentication & Identity Endpoints

### 1.1 Register Customer
- POST /api/v1/auth/register
- Access: Anonymous
- Request Body:
  {
    "fullName": "Jane Doe",
    "email": "jane.doe@example.com",
    "password": "Password123!"
  }
- Response (201 Created):
  {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "jane.doe@example.com",
    "role": "Customer"
  }

### 1.2 Login
- POST /api/v1/auth/login
- Access: Anonymous
- Request Body:
  {
    "email": "admin@tickethub.com",
    "password": "AdminSecurePassword123!"
  }
- Response (200 OK):
  {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5...",
    "refreshToken": "d8e3b1c9-7f32-491a-b601...",
    "expiresInSeconds": 3600
  }

---

## 2. Venues & Seating Layout Endpoints (Admin)

### 2.1 Create Venue
- POST /api/v1/venues
- Access: Admin only
- Request Body:
  {
    "name": "Olympia Arena",
    "city": "London",
    "address": "Blythe Rd, London W14 0UX"
  }
- Response (201 Created):
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Olympia Arena",
    "city": "London",
    "address": "Blythe Rd, London W14 0UX",
    "totalCapacity": 0
  }

### 2.2 Configure Sections & Seats
- POST /api/v1/venues/{venueId}/sections
- Access: Admin only
- Request Body:
  {
    "name": "VIP Floor",
    "rows": [
      { "rowNumber": "A", "seatCount": 20 },
      { "rowNumber": "B", "seatCount": 20 }
    ]
  }
- Response (201 Created):
  {
    "sectionId": "e2a3f789-5717-4562-b3fc-2c963f66bb11",
    "venueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "VIP Floor",
    "totalSeatsGenerated": 40
  }

---

## 3. Events & Catalog Endpoints

### 3.1 Create Event
- POST /api/v1/events
- Access: Admin only
- Request Body:
  {
    "venueId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "title": "Coldplay Music of the Spheres",
    "category": "Concert",
    "description": "Live world tour performance",
    "startDateUtc": "2026-11-20T19:00:00Z",
    "endDateUtc": "2026-11-20T23:00:00Z",
    "sectionPricing": [
      { "sectionId": "e2a3f789-5717-4562-b3fc-2c963f66bb11", "priceCents": 15000 },
      { "sectionId": "a1b2c3d4-5717-4562-b3fc-2c963f66cc22", "priceCents": 7500 }
    ]
  }
- Response (201 Created):
  {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "title": "Coldplay Music of the Spheres",
    "category": "Concert",
    "startDateUtc": "2026-11-20T19:00:00Z"
  }

### 3.2 Search & Browse Events (Public)
- GET /api/v1/events
- Access: Anonymous / All
- Query Parameters: search, city, category, startDateFrom, startDateTo, pageNumber, pageSize
- Response (200 OK):
  {
    "items": [
      {
        "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
        "title": "Coldplay Music of the Spheres",
        "category": "Concert",
        "venueName": "Olympia Arena",
        "city": "London",
        "startDateUtc": "2026-11-20T19:00:00Z",
        "minPriceCents": 7500
      }
    ],
    "pageNumber": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1
  }

### 3.3 Get Event Seating Availability Map
- GET /api/v1/events/{id}/seat-map
- Access: Anonymous / All
- Response (200 OK):
  {
    "eventId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "sections": [
      {
        "sectionId": "e2a3f789-5717-4562-b3fc-2c963f66bb11",
        "sectionName": "VIP Floor",
        "priceCents": 15000,
        "seats": [
          { "seatId": "11111111-7425-40de-944b-e07fc1f90ae7", "row": "A", "number": 1, "status": "Available" },
          { "seatId": "22222222-7425-40de-944b-e07fc1f90ae7", "row": "A", "number": 2, "status": "Held" },
          { "seatId": "33333333-7425-40de-944b-e07fc1f90ae7", "row": "A", "number": 3, "status": "Booked" }
        ]
      }
    ]
  }

---

## 4. Seat Holds & Checkout Endpoints

### 4.1 Create Temporary Seat Hold (10-Minute Lock)
- POST /api/v1/events/{id}/holds
- Access: Authenticated Customer
- Request Body:
  {
    "seatIds": [
      "11111111-7425-40de-944b-e07fc1f90ae7",
      "44444444-7425-40de-944b-e07fc1f90ae7"
    ]
  }
- Response (201 Created):
  {
    "holdId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "eventId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "heldSeats": [
      { "seatId": "11111111-7425-40de-944b-e07fc1f90ae7", "row": "A", "seatNumber": 1, "priceCents": 15000 },
      { "seatId": "44444444-7425-40de-944b-e07fc1f90ae7", "row": "A", "seatNumber": 4, "priceCents": 15000 }
    ],
    "totalPriceCents": 30000,
    "holdExpiresAtUtc": "2026-10-26T22:55:00Z"
  }
- Error Status Codes:
  - 409 Conflict: One or more selected seats are currently unavailable.
  - 422 Unprocessable Entity: Booking would exceed maximum limit of 6 tickets per event.

### 4.2 Execute Checkout & Confirm Booking
- POST /api/v1/bookings/checkout
- Access: Authenticated Customer
- Header: Idempotency-Key: <UUID> (Required)
- Request Body:
  {
    "holdId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "paymentMethod": "CreditCard",
    "paymentToken": "tok_visa_valid"
  }
- Response (200 OK):
  {
    "bookingId": "a7b8c9d0-1234-5678-90ab-cdef12345678",
    "ticketNumber": "TKT-2026-984210",
    "status": "Confirmed",
    "totalPaidCents": 30000,
    "confirmedAtUtc": "2026-10-26T22:47:12Z",
    "qrCodePayload": "ETICKET::TKT-2026-984210::a7b8c9d0"
  }
- Error Status Codes:
  - 400 Bad Request: Missing Idempotency-Key header.
  - 410 Gone: Seat hold has expired. Seats were released.
  - 402 Payment Required: Simulated payment gateway declined transaction.

---

## 5. Booking Self-Service & Cancellation Endpoints

### 5.1 List Customer Bookings
- GET /api/v1/bookings/my-bookings
- Access: Authenticated Customer
- Response (200 OK): Returns list of customer's active and historical bookings.

### 5.2 Cancel Booking (Self-Service)
- POST /api/v1/bookings/{id}/cancel
- Access: Authenticated Customer (Owner only)
- Response (200 OK):
  {
    "bookingId": "a7b8c9d0-1234-5678-90ab-cdef12345678",
    "status": "Cancelled",
    "refundAmountCents": 30000,
    "cancelledAtUtc": "2026-10-26T22:50:00Z"
  }
- Error Status Codes:
  - 422 Unprocessable Entity: Bookings cannot be cancelled within 48 hours of event start.

---

## 6. Executive Reporting Endpoints (Admin)

- GET /api/v1/reports/revenue: Gross revenue breakdown by event and category.
- GET /api/v1/reports/occupancy/{eventId}: Real-time distribution (Sold, Held, Available).
- GET /api/v1/reports/top-events: Top 5 grossing events.
- GET /api/v1/reports/daily-sales?from={date}&to={date}: Daily revenue and sales volume.