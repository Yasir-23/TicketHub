namespace TicketHub.Domain.Exceptions;

/// <summary>
/// BR-01: one or more requested seats are already held or booked for the event (HTTP 409).
/// </summary>
public sealed class SeatAlreadyHeldException : DomainException
{
    public SeatAlreadyHeldException()
        : base("One or more selected seats are currently unavailable.")
    {
        SeatIds = [];
    }

    public SeatAlreadyHeldException(IReadOnlyCollection<Guid> seatIds)
        : base($"One or more selected seats are currently unavailable: {string.Join(", ", seatIds)}.")
    {
        SeatIds = seatIds;
    }

    public IReadOnlyCollection<Guid> SeatIds { get; }
}
