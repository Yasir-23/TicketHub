using MediatR;

namespace TicketHub.Application.Features.Bookings.CancelBooking;

public sealed record CancelBookingCommand(Guid BookingId, Guid CustomerId) : IRequest<CancelBookingResponse>;

/// <summary>The cancellation result and refundable amount.</summary>
public sealed record CancelBookingResponse(
    Guid BookingId,
    string Status,
    long RefundAmountCents,
    DateTimeOffset CancelledAtUtc);
