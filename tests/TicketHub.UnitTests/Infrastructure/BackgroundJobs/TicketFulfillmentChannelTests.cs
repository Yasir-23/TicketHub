using System.Threading.Channels;
using FluentAssertions;
using TicketHub.Application.Common.Models;
using TicketHub.Infrastructure.BackgroundJobs;

namespace TicketHub.UnitTests.Infrastructure.BackgroundJobs;

public sealed class TicketFulfillmentChannelTests
{
    [Fact]
    public void DefaultCapacity_MatchesSdd()
    {
        TicketFulfillmentChannel.DefaultCapacity.Should().Be(5_000);
    }

    [Fact]
    public async Task PublishedMessages_AreReadInOrder()
    {
        var channel = new TicketFulfillmentChannel();
        var messages = Enumerable.Range(1, 3).Select(CreateMessage).ToList();

        foreach (var message in messages)
        {
            await channel.PublishAsync(message);
        }

        channel.Complete();
        var read = await ReadAll(channel);

        read.Should().Equal(messages);
    }

    [Fact]
    public async Task Publish_WhenFull_WaitsUntilAMessageIsConsumed()
    {
        var channel = new TicketFulfillmentChannel(capacity: 2);
        await channel.PublishAsync(CreateMessage(1));
        await channel.PublishAsync(CreateMessage(2));

        var blockedPublish = channel.PublishAsync(CreateMessage(3)).AsTask();

        blockedPublish.IsCompleted.Should().BeFalse("the bounded channel waits instead of dropping messages");
        channel.Count.Should().Be(2);

        await using var reader = channel.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.TicketNumber.Should().Be("TKT-2026-000001");

        await blockedPublish.WaitAsync(TimeSpan.FromSeconds(5));
        channel.Count.Should().Be(2);
    }

    [Fact]
    public async Task Publish_WhenFullAndCancelled_ThrowsWithoutQueueing()
    {
        var channel = new TicketFulfillmentChannel(capacity: 1);
        await channel.PublishAsync(CreateMessage(1));
        using var cts = new CancellationTokenSource();

        var blockedPublish = channel.PublishAsync(CreateMessage(2), cts.Token).AsTask();
        await cts.CancelAsync();

        await blockedPublish.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        channel.Count.Should().Be(1);
    }

    [Fact]
    public async Task Complete_DrainsQueuedMessagesThenRejectsNewOnes()
    {
        var channel = new TicketFulfillmentChannel();
        await channel.PublishAsync(CreateMessage(1));
        await channel.PublishAsync(CreateMessage(2));

        channel.Complete();

        (await ReadAll(channel)).Should().HaveCount(2);
        var lateWrite = () => channel.PublishAsync(CreateMessage(3)).AsTask();
        await lateWrite.Should().ThrowAsync<ChannelClosedException>();
    }

    private static async Task<List<TicketFulfillmentMessage>> ReadAll(TicketFulfillmentChannel channel)
    {
        var read = new List<TicketFulfillmentMessage>();
        await foreach (var message in channel.ReadAllAsync(CancellationToken.None))
        {
            read.Add(message);
        }

        return read;
    }

    internal static TicketFulfillmentMessage CreateMessage(int sequence)
    {
        var bookingId = Guid.NewGuid();
        var ticketNumber = $"TKT-2026-{sequence:D6}";
        return new TicketFulfillmentMessage(
            bookingId,
            Guid.NewGuid(),
            "jane@example.com",
            ticketNumber,
            $"ETICKET::{ticketNumber}::{bookingId.ToString("N")[..8]}",
            "Concert",
            new DateTime(2026, 11, 5, 19, 0, 0, DateTimeKind.Utc));
    }
}
