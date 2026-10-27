using FluentAssertions;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Exceptions;

namespace TicketHub.UnitTests.Domain;

public sealed class BookingReminderTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarkReminderSent_KeepsFirstTimestamp()
    {
        var booking = CreateBooking();
        booking.Confirm(Now);

        booking.MarkReminderSent(Now);
        booking.MarkReminderSent(Now.AddHours(1));

        booking.ReminderSentAtUtc.Should().Be(Now);
    }

    [Fact]
    public void MarkReminderSent_OnUnconfirmedBooking_Throws()
    {
        var booking = CreateBooking();

        var act = () => booking.MarkReminderSent(Now);

        act.Should().Throw<DomainException>();
        booking.ReminderSentAtUtc.Should().BeNull();
    }

    private static Booking CreateBooking() => Booking.Create(Guid.NewGuid(), Guid.NewGuid(), 5000, "TKT-2026-000001");
}
