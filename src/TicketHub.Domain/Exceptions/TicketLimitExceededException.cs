namespace TicketHub.Domain.Exceptions;

/// <summary>
/// BR-02: the request would take the customer over the per-event ticket limit (HTTP 422).
/// </summary>
public sealed class TicketLimitExceededException(int limit, int currentCount, int requestedCount)
    : DomainException(
        $"Booking would exceed the maximum of {limit} tickets per event " +
        $"(currently holding or owning {currentCount}, requested {requestedCount}).");
