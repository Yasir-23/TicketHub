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

        var result = await new GetEventSeatMapQueryHandler(dbContext)
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

        var act = () => new GetEventSeatMapQueryHandler(dbContext)
            .Handle(new GetEventSeatMapQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
