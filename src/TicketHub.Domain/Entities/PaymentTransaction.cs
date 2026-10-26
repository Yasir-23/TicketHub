using TicketHub.Domain.Common;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Domain.Entities;

public sealed class PaymentTransaction : BaseEntity
{
    private PaymentTransaction()
    {
    }

    public Guid BookingId { get; private set; }

    public Booking Booking { get; private set; } = null!;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public long AmountCents { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string ProviderReference { get; private set; } = string.Empty;

    public static PaymentTransaction Initiate(Guid bookingId, string idempotencyKey, long amountCents) =>
        new()
        {
            BookingId = bookingId,
            IdempotencyKey = idempotencyKey,
            AmountCents = amountCents,
            Status = PaymentStatus.Initiated,
            ProviderReference = string.Empty
        };

    public void Complete(PaymentStatus status, string providerReference)
    {
        if (Status != PaymentStatus.Initiated
            || status is not (PaymentStatus.Success or PaymentStatus.Failed or PaymentStatus.TimedOut))
        {
            throw new DomainException($"Payment transaction '{Id}' cannot move from {Status} to {status}.");
        }

        Status = status;
        ProviderReference = providerReference;
    }
}
