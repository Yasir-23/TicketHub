using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Features.Venues.ConfigureSections;
using TicketHub.Application.Features.Venues.CreateVenue;
using TicketHub.Domain.Enums;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Venues;

public sealed class VenueCommandHandlerTests
{
    [Fact]
    public async Task CreateVenue_PersistsVenueWithZeroCapacity()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var handler = new CreateVenueCommandHandler(dbContext);

        var response = await handler.Handle(
            new CreateVenueCommand(" Olympia Arena ", "London", "Blythe Rd, London W14 0UX"),
            CancellationToken.None);

        response.Name.Should().Be("Olympia Arena");
        response.TotalCapacity.Should().Be(0);
        (await dbContext.Venues.SingleAsync()).Id.Should().Be(response.Id);
    }

    [Fact]
    public async Task ConfigureSection_GeneratesAndPersistsSeats()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var venue = await new CreateVenueCommandHandler(dbContext).Handle(
            new CreateVenueCommand("Olympia Arena", "London", "Blythe Rd"), CancellationToken.None);
        var handler = new ConfigureSectionsAndSeatsCommandHandler(dbContext);

        var response = await handler.Handle(
            new ConfigureSectionsAndSeatsCommand(venue.Id, "VIP Floor", [new SectionRowDto("A", 20), new SectionRowDto("B", 20)]),
            CancellationToken.None);

        response.Should().Be(new ConfigureSectionResponse(response.SectionId, venue.Id, "VIP Floor", 40));

        var seats = await dbContext.Seats.Where(s => s.SectionId == response.SectionId).ToListAsync();
        seats.Should().HaveCount(40);
        seats.Should().OnlyContain(s => s.Status == SeatStatus.Available);
        seats.Select(s => (s.RowNumber, s.SeatNumber)).Should().OnlyHaveUniqueItems();

        (await dbContext.Venues.SingleAsync()).TotalCapacity.Should().Be(40);
    }

    [Fact]
    public async Task ConfigureSection_ForUnknownVenue_ThrowsNotFound()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var handler = new ConfigureSectionsAndSeatsCommandHandler(dbContext);

        var act = () => handler.Handle(
            new ConfigureSectionsAndSeatsCommand(Guid.NewGuid(), "VIP", [new SectionRowDto("A", 1)]),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ConfigureSection_WithDuplicateNameIgnoringCase_ThrowsConflict()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var venue = await new CreateVenueCommandHandler(dbContext).Handle(
            new CreateVenueCommand("Olympia Arena", "London", "Blythe Rd"), CancellationToken.None);
        var handler = new ConfigureSectionsAndSeatsCommandHandler(dbContext);
        await handler.Handle(
            new ConfigureSectionsAndSeatsCommand(venue.Id, "VIP", [new SectionRowDto("A", 1)]), CancellationToken.None);

        var act = () => handler.Handle(
            new ConfigureSectionsAndSeatsCommand(venue.Id, "vip", [new SectionRowDto("A", 1)]), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }
}
