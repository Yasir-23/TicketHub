namespace TicketHub.Domain.Exceptions;

public sealed class HoldExpiredException(Guid holdId)
    : DomainException($"Seat hold '{holdId}' has expired.");
