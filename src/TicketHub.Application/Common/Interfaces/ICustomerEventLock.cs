namespace TicketHub.Application.Common.Interfaces;

/// <summary>
/// Serializes hold requests from the same customer for the same event until the current database
/// transaction ends, so concurrent requests cannot both pass the BR-02 ticket-limit check.
/// </summary>
public interface ICustomerEventLock
{
    Task AcquireAsync(Guid customerId, Guid eventId, CancellationToken cancellationToken);
}
