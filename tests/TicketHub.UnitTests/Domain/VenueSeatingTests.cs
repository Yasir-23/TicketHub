using FluentAssertions;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Exceptions;

namespace TicketHub.UnitTests.Domain;

public sealed class VenueSeatingTests
{
    [Fact]
    public void AddSection_GeneratesSeatsForEveryRowNumberedFromOne()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");

        var section = venue.AddSection("VIP Floor", [new SeatRowSpecification("A", 20), new SeatRowSpecification("B", 15)]);

        section.Capacity.Should().Be(35);
        section.VenueId.Should().Be(venue.Id);
        section.Seats.Should().HaveCount(35);
        section.Seats.Should().OnlyContain(s => s.SectionId == section.Id);
        section.Seats.Where(s => s.RowNumber == "A").Select(s => s.SeatNumber)
            .Should().Equal(Enumerable.Range(1, 20));
        section.Seats.Where(s => s.RowNumber == "B").Select(s => s.SeatNumber)
            .Should().Equal(Enumerable.Range(1, 15));
        section.Seats.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AddSection_AccumulatesVenueTotalCapacity()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        venue.TotalCapacity.Should().Be(0);

        venue.AddSection("VIP", [new SeatRowSpecification("A", 10)]);
        venue.AddSection("Balcony", [new SeatRowSpecification("A", 5), new SeatRowSpecification("B", 5)]);

        venue.TotalCapacity.Should().Be(20);
        venue.Sections.Should().HaveCount(2);
    }

    [Fact]
    public void AddSection_NormalizesRowNumbers()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");

        var section = venue.AddSection("Floor", [new SeatRowSpecification(" a ", 2)]);

        section.Seats.Should().OnlyContain(s => s.RowNumber == "A");
    }

    [Fact]
    public void AddSection_WithDuplicateRowNumbers_Throws()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");

        var act = () => venue.AddSection("Floor", [new SeatRowSpecification("A", 2), new SeatRowSpecification("a", 3)]);

        act.Should().Throw<DomainException>().WithMessage("*'A'*more than once*");
        venue.TotalCapacity.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddSection_WithNonPositiveSeatCount_Throws(int seatCount)
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");

        var act = () => venue.AddSection("Floor", [new SeatRowSpecification("A", seatCount)]);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddSection_WithNoRows_Throws()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");

        var act = () => venue.AddSection("Floor", []);

        act.Should().Throw<DomainException>();
    }
}
