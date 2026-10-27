using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using StackExchange.Redis;
using TicketHub.Application.Common.Models;
using TicketHub.Application.Features.Events.GetEvents;
using TicketHub.Application.Features.Events.GetEventSeatMap;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Caching;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Infrastructure.Caching;

public sealed class RedisCacheServiceTests
{
    private readonly IDistributedCache _distributedCache = TestCache.CreateDistributedCache();
    private readonly RedisCacheService _cache;

    public RedisCacheServiceTests() => _cache = TestCache.Create(_distributedCache);

    [Fact]
    public async Task SetThenGet_RoundTripsCatalogPage()
    {
        var page = PagedResult<EventSummaryDto>.Create(
            [
                new EventSummaryDto(
                    Guid.NewGuid(),
                    "Coldplay",
                    EventCategory.Concert,
                    "Wembley",
                    "London",
                    new DateTime(2026, 11, 20, 19, 30, 0, DateTimeKind.Utc),
                    15000),
                new EventSummaryDto(Guid.NewGuid(), "Keynote", EventCategory.Seminar, "ExCeL", "London", DateTime.UtcNow, null)
            ],
            pageNumber: 1,
            pageSize: 20,
            totalCount: 2);

        await _cache.SetAsync("catalog", page);
        var cached = await _cache.GetAsync<PagedResult<EventSummaryDto>>("catalog");

        cached.Should().BeEquivalentTo(page);
        cached!.Items[0].StartDateUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task SetThenGet_RoundTripsSeatMap()
    {
        var seatMap = new EventSeatMapResponse(
            Guid.NewGuid(),
            [new SeatMapSectionDto(Guid.NewGuid(), "VIP", 15000, [new SeatMapSeatDto(Guid.NewGuid(), "A", 1, SeatStatus.Held)])]);

        await _cache.SetAsync("seatmap", seatMap);

        (await _cache.GetAsync<EventSeatMapResponse>("seatmap")).Should().BeEquivalentTo(seatMap);
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsDefault()
    {
        (await _cache.GetAsync<EventSeatMapResponse>("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Get_UnreadableEntry_IsTreatedAsMissAndEvicted()
    {
        await _distributedCache.SetAsync("corrupt", Encoding.UTF8.GetBytes("{not json"), new DistributedCacheEntryOptions());

        var cached = await _cache.GetAsync<EventSeatMapResponse>("corrupt");

        cached.Should().BeNull();
        (await _distributedCache.GetAsync("corrupt")).Should().BeNull();
    }

    [Fact]
    public async Task Remove_EvictsEntry()
    {
        await _cache.SetAsync("key", "value");

        await _cache.RemoveAsync("key");

        (await _cache.GetAsync<string>("key")).Should().BeNull();
    }

    [Fact]
    public async Task GetOrSet_RunsFactoryOnlyOnMiss()
    {
        var calls = 0;
        Task<string> Factory() => Task.FromResult($"value-{++calls}");

        var first = await _cache.GetOrSetAsync("key", Factory);
        var second = await _cache.GetOrSetAsync("key", Factory);

        first.Should().Be("value-1");
        second.Should().Be("value-1");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSet_DoesNotCacheNull()
    {
        var calls = 0;
        Task<string?> Factory()
        {
            calls++;
            return Task.FromResult<string?>(null);
        }

        await _cache.GetOrSetAsync("key", Factory);
        await _cache.GetOrSetAsync("key", Factory);

        calls.Should().Be(2);
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData(5, 5)]
    public async Task Set_AppliesExpirationOrDefault(int? requestedSeconds, int expectedSeconds)
    {
        var backend = new Mock<IDistributedCache>();
        DistributedCacheEntryOptions? captured = null;
        backend
            .Setup(cache => cache.SetAsync("key", It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, options, _) => captured = options)
            .Returns(Task.CompletedTask);

        await TestCache.Create(backend.Object).SetAsync(
            "key",
            "value",
            requestedSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        captured!.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public async Task GetOrSet_WhenCacheBackendIsDown_FallsBackToFactory()
    {
        var backend = new Mock<IDistributedCache>();
        var outage = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis down");
        backend.Setup(cache => cache.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(outage);
        backend
            .Setup(cache => cache.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(outage);

        var value = await TestCache.Create(backend.Object).GetOrSetAsync("key", () => Task.FromResult("from-database"));

        value.Should().Be("from-database");
    }

    [Fact]
    public async Task Get_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var backend = new Mock<IDistributedCache>();
        backend
            .Setup(cache => cache.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => TestCache.Create(backend.Object).GetAsync<string>("key", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
