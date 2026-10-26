using MediatR;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Application.Features.Bookings.CheckoutBooking;

public sealed record CheckoutBookingCommand(
    Guid HoldId,
    Guid CustomerId,
    string PaymentMethod,
    string PaymentToken,
    string IdempotencyKey) : IRequest<CheckoutBookingResponse>, ISensitiveRequest;

/// <summary>The confirmed booking and ticket information returned after checkout.</summary>
public sealed record CheckoutBookingResponse(
    Guid BookingId,
    string TicketNumber,
    string Status,
    long TotalPaidCents,
    DateTimeOffset ConfirmedAtUtc,
    string QrCodePayload);
