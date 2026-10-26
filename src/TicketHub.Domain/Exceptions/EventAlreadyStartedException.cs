namespace TicketHub.Domain.Exceptions;

/// <summary>
/// BR-03: events whose start date has passed are read-only.
/// </summary>
public sealed class EventAlreadyStartedException(Guid eventId)
    : DomainException($"Event '{eventId}' has already started and can no longer be modified or booked.");
