namespace TicketHub.Domain.Exceptions;

public sealed class MissingIdempotencyKeyException()
    : DomainException("The Idempotency-Key header is required.");
