namespace TicketHub.Domain.Exceptions;

/// <summary>
/// BR-03: seats cannot be held or booked once the event has started (HTTP 422).
/// </summary>
public sealed class PastEventBookingException(Guid eventId)
    : DomainException($"Event '{eventId}' has already started; its seats can no longer be held or booked.");
