using FluentAssertions;
using TicketHub.Application.Features.Holds.HoldSeats;

namespace TicketHub.UnitTests.Application.Holds;

public sealed class HoldSeatsCommandValidatorTests
{
    private readonly HoldSeatsCommandValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void Validate_WithOneToSixDistinctSeats_Passes(int seatCount)
    {
        _validator.Validate(Command(Seats(seatCount))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptySeatList_Fails()
    {
        _validator.Validate(Command([])).Errors
            .Should().Contain(e => e.PropertyName == nameof(HoldSeatsCommand.SeatIds));
    }

    [Fact]
    public void Validate_WithNullSeatList_FailsWithoutThrowing()
    {
        _validator.Validate(Command(null!)).Errors
            .Should().Contain(e => e.PropertyName == nameof(HoldSeatsCommand.SeatIds));
    }

    [Fact]
    public void Validate_WithMoreThanSixSeats_Fails()
    {
        _validator.Validate(Command(Seats(7))).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("at most 6"));
    }

    [Fact]
    public void Validate_WithDuplicateSeat_Fails()
    {
        var seatId = Guid.NewGuid();

        _validator.Validate(Command([seatId, seatId])).Errors
            .Should().Contain(e => e.ErrorMessage.Contains("only be requested once"));
    }

    [Fact]
    public void Validate_WithEmptyGuidSeat_Fails()
    {
        _validator.Validate(Command([Guid.Empty])).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithoutCustomerOrEvent_Fails()
    {
        var result = _validator.Validate(new HoldSeatsCommand(Guid.Empty, Guid.Empty, Seats(1)));

        result.Errors.Select(e => e.PropertyName).Should().Contain(
            [nameof(HoldSeatsCommand.EventId), nameof(HoldSeatsCommand.CustomerId)]);
    }

    private static HoldSeatsCommand Command(IReadOnlyList<Guid> seatIds) => new(Guid.NewGuid(), Guid.NewGuid(), seatIds);

    private static List<Guid> Seats(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
}
