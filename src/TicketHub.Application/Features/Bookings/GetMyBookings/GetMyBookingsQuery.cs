using MediatR;

namespace TicketHub.Application.Features.Bookings.GetMyBookings;

public sealed record GetMyBookingsQuery(Guid CustomerId) : IRequest<IReadOnlyList<MyBookingResponse>>;

/// <summary>A booking and the event and ticket details visible to its customer.</summary>
public sealed record MyBookingResponse(
    Guid BookingId,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStartDateUtc,
    string Status,
    long TotalAmountCents,
    string TicketNumber,
    string QrCodePayload,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    IReadOnlyList<MyBookingItemResponse> Items);

/// <summary>A seat and its paid price within a booking.</summary>
public sealed record MyBookingItemResponse(Guid SeatId, string Row, int SeatNumber, long UnitPriceCents);
