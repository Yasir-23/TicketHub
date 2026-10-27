using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using TicketHub.Application.Common.Caching;
using TicketHub.Application.Features.Events.CreateEvent;
using TicketHub.Application.Features.Events.GetEvents;
using TicketHub.Application.Features.Events.GetEventSeatMap;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Caching;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Events;

/// <summary>SDD section 7: tokenized catalog generations and short-lived seat-map caching.</summary>
public sealed class EventCachingTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly AppDbContext _dbContext;
    private readonly RedisCacheService _cache = TestCache.Create();
    private readonly InMemoryCatalogCacheGeneration _generation = new();
    private Venue _venue = null!;
    private Section _section = null!;

    public EventCachingTests() => _dbContext = TestDbContextFactory.Create(_databaseName);

    public async Task InitializeAsync()
    {
        _venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        _section = _venue.AddSection("VIP", [new SeatRowSpecification("A", 2)]);
        _dbContext.Venues.Add(_venue);
        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public void CatalogPageKey_EmbedsGenerationPageAndNormalizedSearch()
    {
        var key = CacheKeys.CatalogPage(7, new GetEventsQuery { PageNumber = 2, Search = "  Rock " });

        key.Should().StartWith("events:catalog:gen:7:page:2:search:rock:");
    }

    [Fact]
    public void CatalogPageKey_DiffersForEveryFilterButIgnoresSearchCase()
    {
        var baseline = new GetEventsQuery { Search = "rock" };

        var keys = new[]
        {
            CacheKeys.CatalogPage(1, baseline),
            CacheKeys.CatalogPage(2, baseline),
            CacheKeys.CatalogPage(1, baseline with { PageNumber = 2 }),
            CacheKeys.CatalogPage(1, baseline with { PageSize = 50 }),
            CacheKeys.CatalogPage(1, baseline with { City = "Paris" }),
            CacheKeys.CatalogPage(1, baseline with { Category = EventCategory.Sports }),
            CacheKeys.CatalogPage(1, baseline with { StartDateFrom = Now }),
            CacheKeys.CatalogPage(1, baseline with { StartDateTo = Now })
        };

        keys.Should().OnlyHaveUniqueItems();
        CacheKeys.CatalogPage(1, baseline with { Search = "ROCK" }).Should().Be(keys[0]);
    }

    [Fact]
    public async Task Catalog_IsServedFromCacheUntilGenerationAdvances()
    {
        AddEvent("First");
        await _dbContext.SaveChangesAsync();
        (await GetEvents()).TotalCount.Should().Be(1);

        // Written straight to the database, bypassing the command that would invalidate the catalog.
        AddEvent("Second");
        await _dbContext.SaveChangesAsync();
        (await GetEvents()).TotalCount.Should().Be(1, "the page is still cached under the current generation");

        await _generation.IncrementAsync();

        (await GetEvents()).TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task CreateEvent_AdvancesGeneration_SoCatalogShowsNewEventImmediately()
    {
        (await GetEvents()).Items.Should().BeEmpty();

        var created = await CreateEventHandler().Handle(ValidCreateCommand(), CancellationToken.None);

        (await _generation.GetCurrentAsync()).Should().Be(1);
        (await GetEvents()).Items.Should().ContainSingle(item => item.Id == created.Id);
    }

    [Fact]
    public async Task CreateEvent_WhenRejected_DoesNotAdvanceGeneration()
    {
        var command = ValidCreateCommand() with { VenueId = Guid.NewGuid() };

        var act = () => CreateEventHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        (await _generation.GetCurrentAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SeatMap_IsCachedBrieflyPerEvent()
    {
        CacheKeys.SeatMapTtl.Should().Be(TimeSpan.FromSeconds(5));
        var @event = AddEvent("Concert");
        await _dbContext.SaveChangesAsync();
        var seat = _section.Seats.First();
        var handler = new GetEventSeatMapQueryHandler(_dbContext, _cache, new FixedTimeProvider(TestDbContextFactory.UtcNow));

        var before = await handler.Handle(new GetEventSeatMapQuery(@event.Id), CancellationToken.None);
        _dbContext.SeatHolds.Add(SeatHold.Create(@event.Id, seat.Id, Guid.NewGuid(), Now.AddMinutes(10)));
        await _dbContext.SaveChangesAsync();
        var cached = await handler.Handle(new GetEventSeatMapQuery(@event.Id), CancellationToken.None);
        await _cache.RemoveAsync(CacheKeys.SeatMap(@event.Id));
        var refreshed = await handler.Handle(new GetEventSeatMapQuery(@event.Id), CancellationToken.None);

        StatusOf(before, seat).Should().Be(SeatStatus.Available);
        StatusOf(cached, seat).Should().Be(SeatStatus.Available);
        StatusOf(refreshed, seat).Should().Be(SeatStatus.Held);
    }

    [Fact]
    public async Task RedisGeneration_UsesAtomicIncrementOnSharedKey()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringIncrementAsync(CacheKeys.CatalogGenerationKey, 1, CommandFlags.None))
            .ReturnsAsync(42);
        database
            .Setup(db => db.StringGetAsync(CacheKeys.CatalogGenerationKey, CommandFlags.None))
            .ReturnsAsync((RedisValue)42);
        var generation = new RedisCatalogCacheGeneration(Multiplexer(database), NullLogger<RedisCatalogCacheGeneration>.Instance);

        (await generation.IncrementAsync()).Should().Be(42);
        (await generation.GetCurrentAsync()).Should().Be(42);
    }

    [Fact]
    public async Task RedisGeneration_WhenRedisIsDown_DegradesInsteadOfThrowing()
    {
        var database = new Mock<IDatabase>();
        var outage = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis down");
        database.Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>())).ThrowsAsync(outage);
        database.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ThrowsAsync(outage);
        var generation = new RedisCatalogCacheGeneration(Multiplexer(database), NullLogger<RedisCatalogCacheGeneration>.Instance);

        (await generation.GetCurrentAsync()).Should().Be(0);
        (await generation.IncrementAsync()).Should().Be(0);
    }

    private static IConnectionMultiplexer Multiplexer(Mock<IDatabase> database)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(database.Object);
        return multiplexer.Object;
    }

    private static SeatStatus StatusOf(EventSeatMapResponse seatMap, Seat seat) =>
        seatMap.Sections.SelectMany(section => section.Seats).Single(s => s.SeatId == seat.Id).Status;

    private Task<TicketHub.Application.Common.Models.PagedResult<EventSummaryDto>> GetEvents() =>
        new GetEventsQueryHandler(_dbContext, _cache, _generation).Handle(new GetEventsQuery(), CancellationToken.None);

    private CreateEventCommandHandler CreateEventHandler() =>
        new(_dbContext, _generation, new FixedTimeProvider(TestDbContextFactory.UtcNow));

    private CreateEventCommand ValidCreateCommand() => new(
        _venue.Id,
        "Coldplay",
        EventCategory.Concert,
        "World tour",
        Now.AddDays(25),
        Now.AddDays(25).AddHours(4),
        [new SectionPriceDto(_section.Id, 15000)]);

    private Event AddEvent(string title)
    {
        var @event = Event.Create(_venue.Id, title, EventCategory.Concert, null, Now.AddDays(3), Now.AddDays(3).AddHours(2), Now);
        @event.SetSectionPrice(_section.Id, 15000, Now);
        _dbContext.Events.Add(@event);
        return @event;
    }
}
