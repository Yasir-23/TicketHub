namespace TicketHub.Domain.Exceptions;

public sealed class CancellationCutoffPassedException()
    : DomainException("Bookings can only be cancelled at least 48 hours before the event.");
