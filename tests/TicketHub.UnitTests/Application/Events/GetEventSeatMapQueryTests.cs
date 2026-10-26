using FluentAssertions;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Features.Events.GetEventSeatMap;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Events;

public sealed class GetEventSeatMapQueryTests
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    [Fact]
    public async Task Handle_ReturnsPricedSectionsWithOrderedSeats()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var vip = venue.AddSection("VIP", [new SeatRowSpecification("AA", 1), new SeatRowSpecification("B", 2)]);
        var balcony = venue.AddSection("Balcony", [new SeatRowSpecification("A", 1)]);
        venue.AddSection("Unpriced", [new SeatRowSpecification("A", 1)]);
        var @event = Event.Create(venue.Id, "Show", EventCategory.Concert, null, Now.AddDays(1), Now.AddDays(1).AddHours(2), Now);
        @event.SetSectionPrice(vip.Id, 15000, Now);
        @event.SetSectionPrice(balcony.Id, 7500, Now);
        dbContext.Venues.Add(venue);
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync();

        var result = await CreateHandler(dbContext)
            .Handle(new GetEventSeatMapQuery(@event.Id), CancellationToken.None);

        result.EventId.Should().Be(@event.Id);
        result.Sections.Select(s => s.SectionName).Should().Equal("Balcony", "VIP");

        var vipMap = result.Sections.Single(s => s.SectionId == vip.Id);
        vipMap.PriceCents.Should().Be(15000);
        vipMap.Seats.Select(s => $"{s.Row}{s.Number}").Should().Equal("B1", "B2", "AA1");
        vipMap.Seats.Should().OnlyContain(s => s.Status == SeatStatus.Available);
    }

    [Fact]
    public async Task Handle_ForUnknownEvent_ThrowsNotFound()
    {
        await using var dbContext = TestDbContextFactory.Create();

        var act = () => CreateHandler(dbContext)
            .Handle(new GetEventSeatMapQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DerivesSeatStatusPerEventFromHolds()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var section = venue.AddSection("VIP", [new SeatRowSpecification("A", 5)]);
        var seats = section.Seats.OrderBy(s => s.SeatNumber).ToList();
        var concert = CreatePricedEvent(venue, section, "Concert");
        var otherEvent = CreatePricedEvent(venue, section, "Other show");
        var customerId = Guid.NewGuid();
        var live = Now.AddMinutes(5);

        var released = SeatHold.Create(concert.Id, seats[3].Id, customerId, live);
        released.Release();
        var converted = SeatHold.Create(concert.Id, seats[1].Id, customerId, live);
        converted.ConvertToBooking();

        dbContext.Venues.Add(venue);
        dbContext.Events.AddRange(concert, otherEvent);
        dbContext.SeatHolds.AddRange(
            SeatHold.Create(concert.Id, seats[0].Id, customerId, live),
            converted,
            SeatHold.Create(concert.Id, seats[2].Id, customerId, Now.AddSeconds(-1)),
            released,
            SeatHold.Create(otherEvent.Id, seats[4].Id, customerId, live));
        await dbContext.SaveChangesAsync();

        var concertMap = await CreateHandler(dbContext).Handle(new GetEventSeatMapQuery(concert.Id), CancellationToken.None);
        var otherMap = await CreateHandler(dbContext).Handle(new GetEventSeatMapQuery(otherEvent.Id), CancellationToken.None);

        concertMap.Sections.Single().Seats.Select(s => s.Status).Should().Equal(
            SeatStatus.Held,      // live hold
            SeatStatus.Booked,    // converted to booking
            SeatStatus.Available, // hold expired, not yet reaped
            SeatStatus.Available, // hold released
            SeatStatus.Available); // held, but for a different event

        otherMap.Sections.Single().Seats.Select(s => s.Status).Should().Equal(
            SeatStatus.Available, SeatStatus.Available, SeatStatus.Available, SeatStatus.Available, SeatStatus.Held);
    }

    private static GetEventSeatMapQueryHandler CreateHandler(TicketHub.Infrastructure.Persistence.AppDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(TestDbContextFactory.UtcNow));

    private static Event CreatePricedEvent(Venue venue, Section section, string title)
    {
        var @event = Event.Create(venue.Id, title, EventCategory.Concert, null, Now.AddDays(1), Now.AddDays(1).AddHours(2), Now);
        @event.SetSectionPrice(section.Id, 15000, Now);
        return @event;
    }
}
