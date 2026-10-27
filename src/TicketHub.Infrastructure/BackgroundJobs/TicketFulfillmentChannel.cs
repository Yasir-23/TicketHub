using System.Threading.Channels;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Common.Models;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// Bounded in-process channel (SDD section 6.2). When full, publishers wait for space rather than dropping
/// tickets, which applies back-pressure to checkout instead of losing confirmed bookings.
/// </summary>
public sealed class TicketFulfillmentChannel : ITicketFulfillmentChannel
{
    public const int DefaultCapacity = 5_000;

    private readonly Channel<TicketFulfillmentMessage> _channel;

    public TicketFulfillmentChannel()
        : this(DefaultCapacity)
    {
    }

    public TicketFulfillmentChannel(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _channel = Channel.CreateBounded<TicketFulfillmentMessage>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Messages queued and not yet read.</summary>
    public int Count => _channel.Reader.Count;

    public ValueTask PublishAsync(TicketFulfillmentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _channel.Writer.WriteAsync(message, ct);
    }

    public IAsyncEnumerable<TicketFulfillmentMessage> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public void Complete() => _channel.Writer.TryComplete();
}
