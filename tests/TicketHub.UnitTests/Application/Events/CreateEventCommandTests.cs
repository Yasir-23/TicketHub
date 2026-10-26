using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Features.Events.CreateEvent;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Events;

public sealed class CreateEventCommandTests
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly CreateEventCommandValidator _validator = new(new FixedTimeProvider(TestDbContextFactory.UtcNow));

    [Fact]
    public void Validator_WithFutureDatesAndPricing_Passes()
    {
        _validator.Validate(ValidCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-24 * 60)]
    public void Validator_WithStartNotInFuture_Fails(int minutesFromNow)
    {
        var start = Now.AddMinutes(minutesFromNow);
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid()) with { StartDateUtc = start, EndDateUtc = start.AddHours(3) };

        _validator.Validate(command).Errors
            .Should().Contain(e => e.PropertyName == nameof(CreateEventCommand.StartDateUtc));
    }

    [Fact]
    public void Validator_WithEndBeforeStart_Fails()
    {
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid()) with { EndDateUtc = Now.AddDays(1) };

        _validator.Validate(command).Errors
            .Should().Contain(e => e.PropertyName == nameof(CreateEventCommand.EndDateUtc));
    }

    [Fact]
    public void Validator_WithNoPricingOrDuplicateSection_Fails()
    {
        var sectionId = Guid.NewGuid();
        var empty = ValidCommand(Guid.NewGuid(), sectionId) with { SectionPricing = [] };
        var duplicate = ValidCommand(Guid.NewGuid(), sectionId) with
        {
            SectionPricing = [new SectionPriceDto(sectionId, 100), new SectionPriceDto(sectionId, 200)]
        };

        _validator.Validate(empty).IsValid.Should().BeFalse();
        _validator.Validate(duplicate).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_WithNegativePriceOrUnknownCategory_Fails()
    {
        var command = ValidCommand(Guid.NewGuid(), Guid.NewGuid()) with
        {
            Category = (EventCategory)99,
            SectionPricing = [new SectionPriceDto(Guid.NewGuid(), -1)]
        };

        var errors = _validator.Validate(command).Errors.Select(e => e.PropertyName).ToList();

        errors.Should().Contain(nameof(CreateEventCommand.Category));
        errors.Should().Contain(p => p.Contains("PriceCents"));
    }

    [Fact]
    public async Task Handler_PersistsEventWithSectionPricing()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var (venueId, sectionId) = await SeedVenueAsync(dbContext);

        var response = await CreateHandler(dbContext).Handle(ValidCommand(venueId, sectionId), CancellationToken.None);

        response.Title.Should().Be("Coldplay Music of the Spheres");
        response.Category.Should().Be(EventCategory.Concert);

        var stored = await dbContext.Events.Include(e => e.SectionPricings).SingleAsync();
        stored.Id.Should().Be(response.Id);
        stored.SectionPricings.Should().ContainSingle(p => p.SectionId == sectionId && p.PriceCents == 15000);
    }

    [Fact]
    public async Task Handler_ForUnknownVenue_ThrowsNotFound()
    {
        await using var dbContext = TestDbContextFactory.Create();

        var act = () => CreateHandler(dbContext).Handle(ValidCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handler_WithSectionFromAnotherVenue_ThrowsValidation()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var (venueId, _) = await SeedVenueAsync(dbContext);
        var (_, otherSectionId) = await SeedVenueAsync(dbContext);

        var act = () => CreateHandler(dbContext).Handle(ValidCommand(venueId, otherSectionId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        (await dbContext.Events.CountAsync()).Should().Be(0);
    }

    private static CreateEventCommandHandler CreateHandler(AppDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(TestDbContextFactory.UtcNow));

    private static CreateEventCommand ValidCommand(Guid venueId, Guid sectionId) => new(
        venueId,
        "Coldplay Music of the Spheres",
        EventCategory.Concert,
        "Live world tour performance",
        Now.AddDays(25),
        Now.AddDays(25).AddHours(4),
        [new SectionPriceDto(sectionId, 15000)]);

    private static async Task<(Guid VenueId, Guid SectionId)> SeedVenueAsync(AppDbContext dbContext)
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var section = venue.AddSection("VIP", [new SeatRowSpecification("A", 2)]);
        dbContext.Venues.Add(venue);
        await dbContext.SaveChangesAsync();
        return (venue.Id, section.Id);
    }
}
