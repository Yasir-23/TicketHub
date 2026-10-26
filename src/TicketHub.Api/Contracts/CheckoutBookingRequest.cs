using System.ComponentModel.DataAnnotations;

namespace TicketHub.Api.Contracts;

/// <summary>Payment details used to check out a held set of seats.</summary>
public sealed record CheckoutBookingRequest
{
    public required Guid HoldId { get; init; }

    [Required, MaxLength(50)]
    public required string PaymentMethod { get; init; }

    [Required, MaxLength(256)]
    public required string PaymentToken { get; init; }
}
