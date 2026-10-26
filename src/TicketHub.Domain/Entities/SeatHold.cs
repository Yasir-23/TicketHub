using TicketHub.Domain.Common;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Domain.Entities;

/// <summary>
/// One seat held by a customer for one event. Seats requested together share a <see cref="HoldGroupId"/>,
/// which is the <c>holdId</c> exposed by the API and later passed to checkout.
/// </summary>
public sealed class SeatHold : BaseEntity
{
    // Required by EF Core.
    private SeatHold()
    {
    }

    public Guid HoldGroupId { get; private set; }

    public Guid EventId { get; private set; }

    public Event Event { get; private set; } = null!;

    public Guid SeatId { get; private set; }

    public Seat Seat { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public User Customer { get; private set; } = null!;

    public DateTime HoldExpiresAtUtc { get; private set; }

    public HoldStatus Status { get; private set; }

    public bool IsActive => IsActiveAt(DateTime.UtcNow);

    public static SeatHold Create(Guid eventId, Guid seatId, Guid customerId, DateTime expiresAtUtc) =>
        Create(eventId, seatId, customerId, expiresAtUtc, Guid.NewGuid());

    public static SeatHold Create(Guid eventId, Guid seatId, Guid customerId, DateTime expiresAtUtc, Guid holdGroupId) =>
        new()
        {
            HoldGroupId = holdGroupId,
            EventId = eventId,
            SeatId = seatId,
            CustomerId = customerId,
            HoldExpiresAtUtc = expiresAtUtc.AsUtc(),
            Status = HoldStatus.Active
        };

    public bool IsActiveAt(DateTime utcNow) => Status == HoldStatus.Active && HoldExpiresAtUtc > utcNow;

    public void Release() => TransitionFromActive(HoldStatus.Released);

    public void Expire() => TransitionFromActive(HoldStatus.Expired);

    public void ConvertToBooking() => TransitionFromActive(HoldStatus.ConvertedToBooking);

    private void TransitionFromActive(HoldStatus target)
    {
        if (Status != HoldStatus.Active)
        {
            throw new DomainException($"Seat hold '{Id}' cannot move from {Status} to {target}.");
        }

        Status = target;
    }
}
