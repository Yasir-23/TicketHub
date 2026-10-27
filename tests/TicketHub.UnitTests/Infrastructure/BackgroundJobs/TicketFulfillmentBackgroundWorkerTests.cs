using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Infrastructure.BackgroundJobs;

public sealed class TicketFulfillmentBackgroundWorkerTests
{
    private readonly TicketFulfillmentChannel _channel = new();
    private readonly RecordingLogger<TicketFulfillmentBackgroundWorker> _logger = new();

    [Fact]
    public async Task Fulfill_WithValidQrPayload_DeliversTicket()
    {
        using var worker = CreateWorker();

        var delivered = await worker.FulfillAsync(TicketFulfillmentChannelTests.CreateMessage(1), CancellationToken.None);

        delivered.Should().BeTrue();
        _logger.Entries.Should().Contain(entry =>
            entry.Message.Contains("TKT-2026-000001") && entry.Message.Contains("emailed to j***@example.com"));
    }

    [Fact]
    public async Task Fulfill_WithTamperedQrPayload_SkipsDelivery()
    {
        using var worker = CreateWorker();
        var message = TicketFulfillmentChannelTests.CreateMessage(1) with { QrPayload = "ETICKET::TKT-2026-999999::deadbeef" };

        var delivered = await worker.FulfillAsync(message, CancellationToken.None);

        delivered.Should().BeFalse();
        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error);
        _logger.Entries.Should().NotContain(entry => entry.Message.Contains("emailed"));
    }

    [Fact]
    public async Task Stop_DrainsMessagesQueuedBeforeShutdown()
    {
        using var worker = CreateWorker();
        for (var sequence = 1; sequence <= 25; sequence++)
        {
            await _channel.PublishAsync(TicketFulfillmentChannelTests.CreateMessage(sequence));
        }

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        _channel.Count.Should().Be(0);
        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logger.Entries.Count(entry => entry.Message.Contains("emailed")).Should().Be(25);
    }

    private TicketFulfillmentBackgroundWorker CreateWorker() =>
        new(
            _channel,
            Options.Create(new TicketFulfillmentOptions
            {
                SimulatedRenderDelay = TimeSpan.Zero,
                SimulatedEmailDelay = TimeSpan.FromMilliseconds(1)
            }),
            TimeProvider.System,
            _logger);
}
