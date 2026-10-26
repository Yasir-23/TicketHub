using FluentAssertions;
using TicketHub.Application.Features.Venues.ConfigureSections;
using TicketHub.Application.Features.Venues.CreateVenue;
using TicketHub.Domain.Entities;

namespace TicketHub.UnitTests.Application.Venues;

public sealed class VenueCommandValidatorTests
{
    private readonly CreateVenueCommandValidator _venueValidator = new();
    private readonly ConfigureSectionsAndSeatsCommandValidator _sectionValidator = new();

    [Fact]
    public void CreateVenue_WithValidCommand_Passes()
    {
        _venueValidator.Validate(new CreateVenueCommand("Olympia Arena", "London", "Blythe Rd, London W14 0UX"))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "London", "Address", nameof(CreateVenueCommand.Name))]
    [InlineData("Arena", "", "Address", nameof(CreateVenueCommand.City))]
    [InlineData("Arena", "London", " ", nameof(CreateVenueCommand.Address))]
    public void CreateVenue_WithMissingField_Fails(string name, string city, string address, string property)
    {
        var result = _venueValidator.Validate(new CreateVenueCommand(name, city, address));

        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    [Fact]
    public void CreateVenue_WithTooLongName_Fails()
    {
        var name = new string('x', VenueConstraints.NameMaxLength + 1);

        _venueValidator.Validate(new CreateVenueCommand(name, "London", "Address"))
            .Errors.Should().Contain(e => e.PropertyName == nameof(CreateVenueCommand.Name));
    }

    [Fact]
    public void ConfigureSection_WithValidRows_Passes()
    {
        var command = new ConfigureSectionsAndSeatsCommand(
            Guid.NewGuid(), "VIP Floor", [new SectionRowDto("A", 20), new SectionRowDto("B", 20)]);

        _sectionValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ConfigureSection_WithNoRows_Fails()
    {
        var result = _sectionValidator.Validate(new ConfigureSectionsAndSeatsCommand(Guid.NewGuid(), "VIP", []));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(ConfigureSectionsAndSeatsCommand.Rows));
    }

    [Fact]
    public void ConfigureSection_WithNullRows_FailsWithoutThrowing()
    {
        var result = _sectionValidator.Validate(new ConfigureSectionsAndSeatsCommand(Guid.NewGuid(), "VIP", null!));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(ConfigureSectionsAndSeatsCommand.Rows));
    }

    [Fact]
    public void ConfigureSection_WithDuplicateRowNumbersIgnoringCase_Fails()
    {
        var command = new ConfigureSectionsAndSeatsCommand(
            Guid.NewGuid(), "VIP", [new SectionRowDto("A", 5), new SectionRowDto("a", 5)]);

        _sectionValidator.Validate(command).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("unique"));
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("A", 0)]
    [InlineData("A", SectionConstraints.MaxSeatsPerRow + 1)]
    [InlineData("ROW-LABEL-TOO-LONG", 10)]
    public void ConfigureSection_WithInvalidRow_Fails(string rowNumber, int seatCount)
    {
        var command = new ConfigureSectionsAndSeatsCommand(Guid.NewGuid(), "VIP", [new SectionRowDto(rowNumber, seatCount)]);

        _sectionValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ConfigureSection_WithEmptyNameAndVenue_Fails()
    {
        var result = _sectionValidator.Validate(
            new ConfigureSectionsAndSeatsCommand(Guid.Empty, "", [new SectionRowDto("A", 1)]));

        result.Errors.Select(e => e.PropertyName).Should().Contain(
            [nameof(ConfigureSectionsAndSeatsCommand.VenueId), nameof(ConfigureSectionsAndSeatsCommand.Name)]);
    }
}
