using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketHub.Application.Features.Events.GetEventSeatMap;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Infrastructure.BackgroundJobs;

public sealed class ExpiredHoldReaperTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly FixedTimeProvider _timeProvider = new(TestDbContextFactory.UtcNow);
    private readonly AppDbContext _dbContext;
    private Event _event = null!;
    private List<Seat> _seats = null!;

    public ExpiredHoldReaperTests() => _dbContext = TestDbContextFactory.Create(_databaseName);

    public async Task InitializeAsync()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var section = venue.AddSection("Floor", [new SeatRowSpecification("A", 8)]);
        _seats = section.Seats.OrderBy(seat => seat.SeatNumber).ToList();
        _event = Event.Create(venue.Id, "Concert", EventCategory.Concert, null, Now.AddDays(5), Now.AddDays(5).AddHours(3), Now);
        _event.SetSectionPrice(section.Id, 5000, Now);

        _dbContext.Venues.Add(venue);
        _dbContext.Events.Add(_event);
        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task ReapBatch_ExpiresOnlyLapsedActiveHolds_OldestFirst_UpToBatchSize()
    {
        var oldest = await AddHold(_seats[0], Now.AddMinutes(-30));
        var older = await AddHold(_seats[1], Now.AddMinutes(-20));
        var newest = await AddHold(_seats[2], Now.AddMinutes(-10));
        var live = await AddHold(_seats[3], Now.AddMinutes(5));
        var converted = await AddHold(_seats[4], Now.AddMinutes(-30), hold => hold.ConvertToBooking());

        var reaped = await new ExpiredHoldReaper(_dbContext, _timeProvider).ReapBatchAsync(2, CancellationToken.None);

        reaped.Should().Be(2);
        (await StatusOf(oldest)).Should().Be(HoldStatus.Expired);
        (await StatusOf(older)).Should().Be(HoldStatus.Expired);
        (await StatusOf(newest)).Should().Be(HoldStatus.Active, "it is beyond this batch and left for the next one");
        (await StatusOf(live)).Should().Be(HoldStatus.Active);
        (await StatusOf(converted)).Should().Be(HoldStatus.ConvertedToBooking);
    }

    [Fact]
    public async Task ReapBatch_ExpiresHoldExpiringExactlyNow()
    {
        var hold = await AddHold(_seats[0], Now);

        var reaped = await new ExpiredHoldReaper(_dbContext, _timeProvider).ReapBatchAsync(100, CancellationToken.None);

        reaped.Should().Be(1);
        (await StatusOf(hold)).Should().Be(HoldStatus.Expired);
    }

    [Fact]
    public async Task ReapBatch_WithNothingExpired_ReturnsZero()
    {
        var live = await AddHold(_seats[0], Now.AddMinutes(1));

        var reaped = await new ExpiredHoldReaper(_dbContext, _timeProvider).ReapBatchAsync(100, CancellationToken.None);

        reaped.Should().Be(0);
        (await StatusOf(live)).Should().Be(HoldStatus.Active);
    }

    [Fact]
    public async Task WorkerCycle_ReapsBacklogInBatches_AndSeatsShowAvailable()
    {
        var expired = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            expired.Add(await AddHold(_seats[index], Now.AddMinutes(-1 - index)));
        }

        var live = await AddHold(_seats[5], Now.AddMinutes(5));

        await using var services = BuildServices();
        var worker = new ExpiredHoldReaperWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExpiredHoldReaperOptions { BatchSize = 2 }),
            _timeProvider,
            NullLogger<ExpiredHoldReaperWorker>.Instance);

        var reaped = await worker.ReapAllAsync(CancellationToken.None);

        reaped.Should().Be(5);
        foreach (var holdId in expired)
        {
            (await StatusOf(holdId)).Should().Be(HoldStatus.Expired);
        }

        (await StatusOf(live)).Should().Be(HoldStatus.Active);

        var seatMap = await new GetEventSeatMapQueryHandler(_dbContext, _timeProvider)
            .Handle(new GetEventSeatMapQuery(_event.Id), CancellationToken.None);
        var statuses = seatMap.Sections.SelectMany(section => section.Seats).ToDictionary(seat => seat.SeatId, seat => seat.Status);
        _seats.Take(5).Should().OnlyContain(seat => statuses[seat.Id] == SeatStatus.Available);
        statuses[_seats[5].Id].Should().Be(SeatStatus.Held);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddDbContext<AppDbContext>(builder => TestDbContextFactory.Configure(builder, _databaseName));
        services.AddScoped<ExpiredHoldReaper>();
        return services.BuildServiceProvider();
    }

    private async Task<Guid> AddHold(Seat seat, DateTime expiresAtUtc, Action<SeatHold>? configure = null)
    {
        var hold = SeatHold.Create(_event.Id, seat.Id, Guid.NewGuid(), expiresAtUtc);
        configure?.Invoke(hold);
        _dbContext.SeatHolds.Add(hold);
        await _dbContext.SaveChangesAsync();
        return hold.Id;
    }

    // A fresh context sees committed state rather than the test context's tracked entities.
    private async Task<HoldStatus> StatusOf(Guid holdId)
    {
        await using var context = TestDbContextFactory.Create(_databaseName);
        return (await context.SeatHolds.SingleAsync(hold => hold.Id == holdId)).Status;
    }
}
