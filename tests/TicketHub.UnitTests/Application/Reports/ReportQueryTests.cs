using FluentAssertions;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Features.Reports.GetDailySalesReport;
using TicketHub.Application.Features.Reports.GetEventOccupancyReport;
using TicketHub.Application.Features.Reports.GetRevenueReport;
using TicketHub.Application.Features.Reports.GetTopGrossingEvents;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Reports;

public sealed class ReportQueryTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly AppDbContext _dbContext;
    private Venue _venue = null!;
    private List<Seat> _vip = null!;
    private List<Seat> _floor = null!;
    private Section _vipSection = null!;
    private Section _floorSection = null!;
    private Section _balconySection = null!;

    public ReportQueryTests() => _dbContext = TestDbContextFactory.Create(_databaseName);

    public async Task InitializeAsync()
    {
        _venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        _vipSection = _venue.AddSection("VIP", [new SeatRowSpecification("A", 4)]);
        _floorSection = _venue.AddSection("Floor", [new SeatRowSpecification("B", 6)]);
        // Never priced in these tests, so never on sale and never counted.
        _balconySection = _venue.AddSection("Balcony", [new SeatRowSpecification("C", 5)]);
        _vip = _vipSection.Seats.OrderBy(s => s.SeatNumber).ToList();
        _floor = _floorSection.Seats.OrderBy(s => s.SeatNumber).ToList();

        _dbContext.Venues.Add(_venue);
        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    // ---- FR-REP-01: revenue ----

    [Fact]
    public async Task Revenue_SumsConfirmedBookingsOnly_GroupedByCategoryAndEvent()
    {
        var concert = await AddEvent("Coldplay", EventCategory.Concert);
        var match = await AddEvent("Cup Final", EventCategory.Sports);
        await AddBooking(concert, [_vip[0], _vip[1]], 100);
        await AddBooking(concert, [_floor[0]], 150);
        await AddBooking(concert, [_floor[1]], 500, BookingStatus.Cancelled);
        await AddBooking(concert, [_floor[2]], 700, BookingStatus.PendingPayment);
        await AddBooking(match, [_vip[2]], 300);

        var report = await new GetRevenueReportQueryHandler(_dbContext).Handle(new GetRevenueReportQuery(), CancellationToken.None);

        report.TotalRevenueCents.Should().Be(650);
        report.TotalTicketsSold.Should().Be(4);
        report.TotalBookings.Should().Be(3);
        report.Categories.Select(c => c.Category).Should().Equal(EventCategory.Concert, EventCategory.Sports);

        var concerts = report.Categories[0];
        concerts.RevenueCents.Should().Be(350);
        concerts.TicketsSold.Should().Be(3);
        concerts.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            EventId = concert.Id,
            Title = "Coldplay",
            RevenueCents = 350L,
            TicketsSold = 3,
            BookingCount = 2
        });
        report.Categories[1].Events.Single().RevenueCents.Should().Be(300);
    }

    [Fact]
    public async Task Revenue_WithNoConfirmedSales_IsEmpty()
    {
        var concert = await AddEvent("Coldplay", EventCategory.Concert);
        await AddBooking(concert, [_vip[0]], 100, BookingStatus.Cancelled);

        var report = await new GetRevenueReportQueryHandler(_dbContext).Handle(new GetRevenueReportQuery(), CancellationToken.None);

        report.TotalRevenueCents.Should().Be(0);
        report.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task Revenue_IncludesSoftDeletedEventsThatEarnedMoney()
    {
        var concert = await AddEvent("Retired Show", EventCategory.Concert);
        await AddBooking(concert, [_vip[0]], 100);
        await using (var admin = TestDbContextFactory.Create(_databaseName))
        {
            admin.Events.Remove(admin.Events.Single(e => e.Id == concert.Id));
            await admin.SaveChangesAsync();
        }

        var report = await new GetRevenueReportQueryHandler(_dbContext).Handle(new GetRevenueReportQuery(), CancellationToken.None);

        report.Categories.Single().Events.Single().Title.Should().Be("Retired Show");
    }

    // ---- FR-REP-02: occupancy ----

    [Fact]
    public async Task Occupancy_BreaksDownSoldHeldAvailable_OverallAndPerSection()
    {
        var concert = await AddEvent("Coldplay", EventCategory.Concert);
        await AddBooking(concert, [_vip[0], _vip[1]], 100);
        await AddBooking(concert, [_floor[0]], 100);
        await AddBooking(concert, [_floor[1]], 100, BookingStatus.Cancelled);
        await AddHold(concert, _floor[2], Now.AddMinutes(5));
        await AddHold(concert, _floor[3], Now.AddSeconds(-1));
        await AddHold(concert, _floor[4], Now.AddMinutes(5), hold => hold.Release());

        var report = await Occupancy(concert.Id);

        report.Should().BeEquivalentTo(new
        {
            EventId = concert.Id,
            Title = "Coldplay",
            TotalSeats = 10,
            SoldSeats = 3,
            HeldSeats = 1,
            AvailableSeats = 6,
            OccupancyPercentage = 30.00m
        });
        report.Sections.Should().BeEquivalentTo(
            [
                new SectionOccupancyDto(_floorSection.Id, "Floor", 6, 1, 1, 4, 16.67m),
                new SectionOccupancyDto(_vipSection.Id, "VIP", 4, 2, 0, 2, 50.00m)
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Occupancy_IgnoresOtherEventsAndUnpricedSections()
    {
        var concert = await AddEvent("Coldplay", EventCategory.Concert);
        var other = await AddEvent("Other", EventCategory.Concert);
        await AddBooking(other, [_vip[0]], 100);
        await AddHold(other, _vip[1], Now.AddMinutes(5));

        var report = await Occupancy(concert.Id);

        report.TotalSeats.Should().Be(10, "Balcony is not priced for this event");
        report.SoldSeats.Should().Be(0);
        report.HeldSeats.Should().Be(0);
        report.AvailableSeats.Should().Be(10);
        report.OccupancyPercentage.Should().Be(0m);
        report.Sections.Should().NotContain(s => s.SectionId == _balconySection.Id);
    }

    [Fact]
    public async Task Occupancy_RoundsPercentageToTwoDecimals()
    {
        var concert = await AddEvent("Small Gig", EventCategory.Concert, priceFloor: false);
        await AddBooking(concert, [_vip[0]], 100);
        await AddBooking(concert, [_vip[1]], 100);

        var report = await Occupancy(concert.Id);

        report.TotalSeats.Should().Be(4);
        report.OccupancyPercentage.Should().Be(50.00m);

        await AddBooking(concert, [_vip[2]], 100);
        (await Occupancy(concert.Id)).OccupancyPercentage.Should().Be(75.00m);

        var floorOnly = await AddEvent("Floor Show", EventCategory.Concert, priceVip: false);
        await AddBooking(floorOnly, [_floor[0]], 100);
        await AddBooking(floorOnly, [_floor[1]], 100);
        await AddBooking(floorOnly, [_floor[2]], 100);
        await AddBooking(floorOnly, [_floor[3]], 100);
        (await Occupancy(floorOnly.Id)).OccupancyPercentage.Should().Be(66.67m, "4 of 6 seats is 66.666…%");
    }

    [Fact]
    public async Task Occupancy_ForUnknownEvent_ThrowsNotFound()
    {
        var act = () => Occupancy(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- FR-REP-03: top 5 ----

    [Fact]
    public async Task TopEvents_ReturnsFiveHighestGrossingInDescendingOrder()
    {
        int[] revenues = [400, 900, 100, 700, 300, 800, 200];
        var events = new List<Event>();
        for (var index = 0; index < revenues.Length; index++)
        {
            var @event = await AddEvent($"Event {index}", EventCategory.Concert);
            await AddBooking(@event, [_vip[0]], revenues[index]);
            events.Add(@event);
        }

        // A large cancelled booking must not lift the lowest-grossing event into the ranking.
        await AddBooking(events[2], [_vip[1]], 10_000, BookingStatus.Cancelled);

        var top = await TopEvents();

        top.Select(t => t.RevenueCents).Should().Equal(900, 800, 700, 400, 300);
        top.Select(t => t.Rank).Should().Equal(1, 2, 3, 4, 5);
        top[0].Should().BeEquivalentTo(new { EventId = events[1].Id, Title = "Event 1", TicketsSold = 1, BookingCount = 1 });
    }

    [Fact]
    public async Task TopEvents_WithFewerThanFiveEvents_ReturnsThoseWithSales()
    {
        var first = await AddEvent("First", EventCategory.Concert);
        var second = await AddEvent("Second", EventCategory.Sports);
        await AddEvent("Unsold", EventCategory.Seminar);
        await AddBooking(first, [_vip[0], _vip[1]], 100);
        await AddBooking(second, [_vip[2]], 500);

        var top = await TopEvents();

        top.Select(t => t.EventId).Should().Equal(second.Id, first.Id);
        top[1].TicketsSold.Should().Be(2);
    }

    [Fact]
    public async Task TopEvents_BreaksRevenueTiesByBookingCount()
    {
        var single = await AddEvent("Single", EventCategory.Concert);
        var split = await AddEvent("Split", EventCategory.Concert);
        await AddBooking(single, [_vip[0]], 200);
        await AddBooking(split, [_vip[1]], 100);
        await AddBooking(split, [_vip[2]], 100);

        var top = await TopEvents();

        top.Select(t => t.EventId).Should().Equal(split.Id, single.Id);
    }

    // ---- FR-REP-04: daily sales ----

    [Theory]
    [InlineData("2026-10-20", "2026-10-20", true)]
    [InlineData("2026-10-20", "2026-10-22", true)]
    [InlineData("2026-10-22", "2026-10-20", false)]
    [InlineData("2026-01-01", "2026-12-31", true)]
    [InlineData("2026-01-01", "2027-01-01", true)]
    [InlineData("2026-01-01", "2027-01-02", false)]
    public void DailySalesValidator_ChecksOrderAndMaximumRange(string from, string to, bool valid)
    {
        var query = new GetDailySalesReportQuery { From = DateOnly.Parse(from), To = DateOnly.Parse(to) };

        new GetDailySalesReportQueryValidator().Validate(query).IsValid.Should().Be(valid);
    }

    [Fact]
    public void DailySalesValidator_ReportsInvertedRangeAgainstTo()
    {
        var result = new GetDailySalesReportQueryValidator()
            .Validate(new GetDailySalesReportQuery { From = new DateOnly(2026, 10, 22), To = new DateOnly(2026, 10, 20) });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(GetDailySalesReportQuery.To));
    }

    [Fact]
    public void DailySalesValidator_RequiresBothDates()
    {
        var result = new GetDailySalesReportQueryValidator().Validate(new GetDailySalesReportQuery());

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            [nameof(GetDailySalesReportQuery.From), nameof(GetDailySalesReportQuery.To)]);
    }

    [Fact]
    public async Task DailySales_AggregatesConfirmedSalesPerUtcDay_InclusiveRange_ZeroFilled()
    {
        var concert = await AddEvent("Coldplay", EventCategory.Concert);
        await AddBooking(concert, [_vip[0]], 999, confirmedAtUtc: Utc(2026, 10, 19, 23, 59, 59));
        await AddBooking(concert, [_vip[1], _vip[2]], 100, confirmedAtUtc: Utc(2026, 10, 20, 0, 0, 0));
        await AddBooking(concert, [_vip[3]], 150, confirmedAtUtc: Utc(2026, 10, 20, 23, 59, 59));
        await AddBooking(concert, [_floor[0]], 400, BookingStatus.Cancelled, Utc(2026, 10, 21, 12, 0, 0));
        await AddBooking(concert, [_floor[1]], 300, confirmedAtUtc: Utc(2026, 10, 22, 23, 59, 59));
        await AddBooking(concert, [_floor[2]], 999, confirmedAtUtc: Utc(2026, 10, 23, 0, 0, 0));

        var report = await new GetDailySalesReportQueryHandler(_dbContext).Handle(
            new GetDailySalesReportQuery { From = new DateOnly(2026, 10, 20), To = new DateOnly(2026, 10, 22) },
            CancellationToken.None);

        report.Days.Should().Equal(
            new DailySalesDto(new DateOnly(2026, 10, 20), BookingCount: 2, TicketsSold: 3, RevenueCents: 350),
            new DailySalesDto(new DateOnly(2026, 10, 21), 0, 0, 0),
            new DailySalesDto(new DateOnly(2026, 10, 22), 1, 1, 300));
        report.TotalRevenueCents.Should().Be(650);
        report.TotalBookings.Should().Be(3);
        report.TotalTicketsSold.Should().Be(4);
    }

    private Task<EventOccupancyReportResponse> Occupancy(Guid eventId) =>
        new GetEventOccupancyReportQueryHandler(_dbContext, new FixedTimeProvider(TestDbContextFactory.UtcNow))
            .Handle(new GetEventOccupancyReportQuery(eventId), CancellationToken.None);

    private Task<IReadOnlyList<TopGrossingEventDto>> TopEvents() =>
        new GetTopGrossingEventsQueryHandler(_dbContext).Handle(new GetTopGrossingEventsQuery(), CancellationToken.None);

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private async Task<Event> AddEvent(string title, EventCategory category, bool priceVip = true, bool priceFloor = true)
    {
        var @event = Event.Create(_venue.Id, title, category, null, Now.AddDays(10), Now.AddDays(10).AddHours(3), Now);
        if (priceVip)
        {
            @event.SetSectionPrice(_vipSection.Id, 100, Now);
        }

        if (priceFloor)
        {
            @event.SetSectionPrice(_floorSection.Id, 100, Now);
        }

        _dbContext.Events.Add(@event);
        await _dbContext.SaveChangesAsync();
        return @event;
    }

    /// <param name="unitPriceCents">Price per seat; the booking total is the sum over seats.</param>
    private async Task AddBooking(
        Event @event,
        IReadOnlyList<Seat> seats,
        long unitPriceCents,
        BookingStatus status = BookingStatus.Confirmed,
        DateTime? confirmedAtUtc = null)
    {
        var booking = Booking.Create(@event.Id, Guid.NewGuid(), unitPriceCents * seats.Count, $"TKT-{Guid.NewGuid():N}"[..20]);
        foreach (var seat in seats)
        {
            booking.AddBookingItem(seat.Id, unitPriceCents);
        }

        if (status != BookingStatus.PendingPayment)
        {
            booking.Confirm(confirmedAtUtc ?? Now);
        }

        if (status == BookingStatus.Cancelled)
        {
            booking.Cancel(Now);
        }

        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync();
    }

    private async Task AddHold(Event @event, Seat seat, DateTime expiresAtUtc, Action<SeatHold>? configure = null)
    {
        var hold = SeatHold.Create(@event.Id, seat.Id, Guid.NewGuid(), expiresAtUtc);
        configure?.Invoke(hold);
        _dbContext.SeatHolds.Add(hold);
        await _dbContext.SaveChangesAsync();
    }
}
