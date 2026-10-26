using FluentAssertions;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.UnitTests.Domain;

public sealed class SeatHoldTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_StartsActiveWithGivenExpiry()
    {
        var hold = SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10));

        hold.Status.Should().Be(HoldStatus.Active);
        hold.HoldExpiresAtUtc.Should().Be(Now.AddMinutes(10));
        hold.HoldGroupId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_WithGroupId_SharesGroupAcrossSeats()
    {
        var groupId = Guid.NewGuid();

        var first = SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, groupId);
        var second = SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, groupId);

        first.HoldGroupId.Should().Be(groupId);
        second.HoldGroupId.Should().Be(groupId);
        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void IsActiveAt_IsFalseFromTheExactExpiryInstant()
    {
        var hold = SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10));

        hold.IsActiveAt(Now.AddMinutes(10).AddTicks(-1)).Should().BeTrue();
        hold.IsActiveAt(Now.AddMinutes(10)).Should().BeFalse();
    }

    [Fact]
    public void IsActive_UsesCurrentClock()
    {
        SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddMinutes(10))
            .IsActive.Should().BeTrue();
        SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1))
            .IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(HoldStatus.Released)]
    [InlineData(HoldStatus.Expired)]
    [InlineData(HoldStatus.ConvertedToBooking)]
    public void Transitions_FromActive_SetStatusAndDeactivate(HoldStatus target)
    {
        var hold = SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10));

        Transition(hold, target);

        hold.Status.Should().Be(target);
        hold.IsActiveAt(Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(HoldStatus.Released, HoldStatus.ConvertedToBooking)]
    [InlineData(HoldStatus.Expired, HoldStatus.ConvertedToBooking)]
    [InlineData(HoldStatus.ConvertedToBooking, HoldStatus.Released)]
    [InlineData(HoldStatus.Expired, HoldStatus.Expired)]
    public void Transitions_FromNonActive_Throw(HoldStatus first, HoldStatus second)
    {
        var hold = SeatHold.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10));
        Transition(hold, first);

        var act = () => Transition(hold, second);

        act.Should().Throw<DomainException>();
        hold.Status.Should().Be(first);
    }

    private static void Transition(SeatHold hold, HoldStatus target)
    {
        switch (target)
        {
            case HoldStatus.Released:
                hold.Release();
                break;
            case HoldStatus.Expired:
                hold.Expire();
                break;
            case HoldStatus.ConvertedToBooking:
                hold.ConvertToBooking();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
