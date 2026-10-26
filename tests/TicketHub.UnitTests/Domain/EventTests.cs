using FluentAssertions;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.UnitTests.Domain;

public sealed class EventTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithFutureDates_Succeeds()
    {
        var @event = CreateEvent(Now.AddDays(10));

        @event.StartDateUtc.Should().Be(Now.AddDays(10));
        @event.HasStarted(Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Create_WithStartNotInFuture_Throws(int minutesFromNow)
    {
        var act = () => CreateEvent(Now.AddMinutes(minutesFromNow));

        act.Should().Throw<DomainException>().WithMessage("*future*");
    }

    [Fact]
    public void Create_WithEndBeforeStart_Throws()
    {
        var act = () => Event.Create(
            Guid.NewGuid(), "Show", EventCategory.Concert, null, Now.AddDays(2), Now.AddDays(1), Now);

        act.Should().Throw<DomainException>().WithMessage("*end date*");
    }

    [Fact]
    public void Create_WithUnspecifiedKind_TreatsValuesAsUtc()
    {
        var start = DateTime.SpecifyKind(Now.AddDays(1), DateTimeKind.Unspecified);

        var @event = Event.Create(Guid.NewGuid(), "Show", EventCategory.Concert, null, start, start.AddHours(2), Now);

        @event.StartDateUtc.Kind.Should().Be(DateTimeKind.Utc);
        @event.StartDateUtc.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void HasStarted_AtExactStartTime_IsTrue()
    {
        var @event = CreateEvent(Now.AddHours(1));

        @event.HasStarted(Now.AddHours(1)).Should().BeTrue();
    }

    [Fact]
    public void SetSectionPrice_AfterEventStarted_ThrowsEventAlreadyStarted()
    {
        var @event = CreateEvent(Now.AddHours(1));

        var act = () => @event.SetSectionPrice(Guid.NewGuid(), 5000, Now.AddHours(2));

        act.Should().Throw<EventAlreadyStartedException>();
    }

    [Fact]
    public void SetSectionPrice_ForSameSectionTwice_UpdatesExistingPrice()
    {
        var @event = CreateEvent(Now.AddDays(1));
        var sectionId = Guid.NewGuid();

        @event.SetSectionPrice(sectionId, 5000, Now);
        @event.SetSectionPrice(sectionId, 7500, Now);

        @event.SectionPricings.Should().ContainSingle()
            .Which.Should().Match<EventSectionPricing>(p => p.SectionId == sectionId && p.PriceCents == 7500);
    }

    [Fact]
    public void SetSectionPrice_WithNegativePrice_Throws()
    {
        var @event = CreateEvent(Now.AddDays(1));

        var act = () => @event.SetSectionPrice(Guid.NewGuid(), -1, Now);

        act.Should().Throw<DomainException>();
    }

    private static Event CreateEvent(DateTime start) =>
        Event.Create(Guid.NewGuid(), "Show", EventCategory.Concert, "desc", start, start.AddHours(3), Now);
}
