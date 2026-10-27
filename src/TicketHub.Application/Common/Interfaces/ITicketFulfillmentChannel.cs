using TicketHub.Application.Common.Models;

namespace TicketHub.Application.Common.Interfaces;

/// <summary>
/// In-process queue that decouples ticket rendering and email delivery from the checkout request path
/// (FR-JOB-02, SDD section 6.2).
/// </summary>
public interface ITicketFulfillmentChannel
{
    /// <summary>Queues a message, waiting for space while the channel is at capacity.</summary>
    ValueTask PublishAsync(TicketFulfillmentMessage message, CancellationToken ct = default);

    /// <summary>Yields queued messages until the channel is completed and drained, or <paramref name="ct"/> fires.</summary>
    IAsyncEnumerable<TicketFulfillmentMessage> ReadAllAsync(CancellationToken ct);

    /// <summary>
    /// Stops accepting new messages. Messages already queued are still yielded by <see cref="ReadAllAsync"/>,
    /// which then ends, so a consumer can drain the channel on shutdown.
    /// </summary>
    void Complete();
}
