using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Moq;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Features.Holds.HoldSeats;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Holds;

public sealed class HoldSeatsCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly AppDbContext _dbContext = TestDbContextFactory.Create();
    private readonly Mock<ICustomerEventLock> _customerEventLock = new();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _rivalId = Guid.NewGuid();

    private Event _event = null!;
    private Event _pastEvent = null!;
    private List<Seat> _vipSeats = null!;
    private List<Seat> _balconySeats = null!;
    private Seat _unpricedSeat = null!;

    public async Task InitializeAsync()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var vip = venue.AddSection("VIP", [new SeatRowSpecification("A", 10)]);
        var balcony = venue.AddSection("Balcony", [new SeatRowSpecification("A", 5)]);
        var unpriced = venue.AddSection("Restricted", [new SeatRowSpecification("A", 1)]);
        _vipSeats = vip.Seats.OrderBy(s => s.SeatNumber).ToList();
        _balconySeats = balcony.Seats.OrderBy(s => s.SeatNumber).ToList();
        _unpricedSeat = unpriced.Seats.Single();

        _event = Event.Create(venue.Id, "Concert", EventCategory.Concert, null, Now.AddDays(10), Now.AddDays(10).AddHours(3), Now);
        _event.SetSectionPrice(vip.Id, 15000, Now);
        _event.SetSectionPrice(balcony.Id, 7500, Now);

        // Created a day ago for a start time that has now passed.
        var yesterday = Now.AddDays(-1);
        _pastEvent = Event.Create(venue.Id, "Started", EventCategory.Concert, null, Now.AddHours(-1), Now.AddHours(2), yesterday);
        _pastEvent.SetSectionPrice(vip.Id, 15000, yesterday);

        _dbContext.Users.AddRange(
            User.CreateCustomer("Jane", "jane@example.com", "hash"),
            User.CreateCustomer("Rival", "rival@example.com", "hash"));
        _dbContext.Venues.Add(venue);
        _dbContext.Events.AddRange(_event, _pastEvent);
        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task Handle_HoldsSeatsAndRollsUpPricing()
    {
        var response = await Handle(_customerId, _vipSeats[1].Id, _vipSeats[0].Id, _balconySeats[0].Id);

        response.EventId.Should().Be(_event.Id);
        response.HeldSeats.Select(s => (s.Row, s.SeatNumber, s.PriceCents)).Should().BeEquivalentTo(
            [("A", 1, 15000L), ("A", 2, 15000L), ("A", 1, 7500L)]);
        response.TotalPriceCents.Should().Be(37500);

        var holds = await _dbContext.SeatHolds.ToListAsync();
        holds.Should().HaveCount(3);
        holds.Should().OnlyContain(h =>
            h.HoldGroupId == response.HoldId
            && h.EventId == _event.Id
            && h.CustomerId == _customerId
            && h.Status == HoldStatus.Active);
    }

    [Fact]
    public async Task Handle_SetsExpiryExactlyTenMinutesFromNow()
    {
        var response = await Handle(_customerId, _vipSeats[0].Id);

        response.HoldExpiresAtUtc.Should().Be(Now.AddMinutes(10));
        response.HoldExpiresAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        (await _dbContext.SeatHolds.SingleAsync()).HoldExpiresAtUtc.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public async Task Handle_AcquiresCustomerEventLockBeforeHolding()
    {
        await Handle(_customerId, _vipSeats[0].Id);

        _customerEventLock.Verify(l => l.AcquireAsync(_customerId, _event.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCustomerHoldsFourAndRequestsThree_ThrowsTicketLimitExceeded()
    {
        await Handle(_customerId, _vipSeats.Take(4).Select(s => s.Id).ToArray());

        var act = () => Handle(_customerId, _vipSeats.Skip(4).Take(3).Select(s => s.Id).ToArray());

        await act.Should().ThrowAsync<TicketLimitExceededException>().WithMessage("*currently holding or owning 4, requested 3*");
        (await _dbContext.SeatHolds.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Handle_WhenCustomerHoldsFourAndRequestsTwo_ReachesLimitExactly()
    {
        await Handle(_customerId, _vipSeats.Take(4).Select(s => s.Id).ToArray());

        await Handle(_customerId, _vipSeats.Skip(4).Take(2).Select(s => s.Id).ToArray());

        (await _dbContext.SeatHolds.CountAsync(h => h.CustomerId == _customerId)).Should().Be(6);
    }

    [Fact]
    public async Task Handle_TicketLimit_CountsBookedSeatsButNotExpiredOrReleasedHolds()
    {
        var expired = SeatHold.Create(_event.Id, _vipSeats[0].Id, _customerId, Now.AddMinutes(-1));
        var released = SeatHold.Create(_event.Id, _vipSeats[1].Id, _customerId, Now.AddMinutes(5));
        released.Release();
        var booked = Enumerable.Range(2, 5)
            .Select(i => SeatHold.Create(_event.Id, _vipSeats[i].Id, _customerId, Now.AddMinutes(5)))
            .ToList();
        booked.ForEach(h => h.ConvertToBooking());
        _dbContext.SeatHolds.AddRange([expired, released, .. booked]);
        await _dbContext.SaveChangesAsync();

        await Handle(_customerId, _vipSeats[7].Id); // 5 booked + 1 = 6: allowed

        var act = () => Handle(_customerId, _vipSeats[8].Id);
        await act.Should().ThrowAsync<TicketLimitExceededException>();
    }

    [Fact]
    public async Task Handle_TicketLimit_IsPerCustomerAndPerEvent()
    {
        await Handle(_rivalId, _vipSeats.Take(6).Select(s => s.Id).ToArray());

        var response = await Handle(_customerId, _vipSeats.Skip(6).Take(4).Select(s => s.Id).ToArray());

        response.HeldSeats.Should().HaveCount(4);
    }

    [Fact]
    public async Task Handle_ForStartedEvent_ThrowsPastEventBooking()
    {
        var act = () => Send(new HoldSeatsCommand(_pastEvent.Id, _customerId, [_vipSeats[0].Id]));

        await act.Should().ThrowAsync<PastEventBookingException>();
        (await _dbContext.SeatHolds.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ForUnknownEvent_ThrowsNotFound()
    {
        var act = () => Send(new HoldSeatsCommand(Guid.NewGuid(), _customerId, [_vipSeats[0].Id]));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WithSeatOutsidePricedSections_ThrowsValidation()
    {
        var act = () => Handle(_customerId, _vipSeats[0].Id, _unpricedSeat.Id);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains(_unpricedSeat.Id.ToString()));
        (await _dbContext.SeatHolds.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenSeatAlreadyHeldByAnotherCustomer_ThrowsConflictAndHoldsNothing()
    {
        await Handle(_rivalId, _vipSeats[0].Id);

        var act = () => Handle(_customerId, _vipSeats[1].Id, _vipSeats[0].Id);

        (await act.Should().ThrowAsync<SeatAlreadyHeldException>())
            .Which.SeatIds.Should().Equal(_vipSeats[0].Id);
        (await _dbContext.SeatHolds.CountAsync(h => h.CustomerId == _customerId)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenSeatAlreadyBooked_ThrowsConflict()
    {
        var booked = SeatHold.Create(_event.Id, _vipSeats[0].Id, _rivalId, Now.AddMinutes(-30));
        booked.ConvertToBooking();
        _dbContext.SeatHolds.Add(booked);
        await _dbContext.SaveChangesAsync();

        var act = () => Handle(_customerId, _vipSeats[0].Id);

        await act.Should().ThrowAsync<SeatAlreadyHeldException>();
    }

    [Fact]
    public async Task Handle_WhenPreviousHoldExpired_ExpiresItAndHoldsSeat()
    {
        var stale = SeatHold.Create(_event.Id, _vipSeats[0].Id, _rivalId, Now.AddSeconds(-1));
        _dbContext.SeatHolds.Add(stale);
        await _dbContext.SaveChangesAsync();

        var response = await Handle(_customerId, _vipSeats[0].Id);

        response.HeldSeats.Should().ContainSingle(s => s.SeatId == _vipSeats[0].Id);
        (await _dbContext.SeatHolds.SingleAsync(h => h.Id == stale.Id)).Status.Should().Be(HoldStatus.Expired);
        (await _dbContext.SeatHolds.CountAsync(h => h.Status == HoldStatus.Active)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_SameSeatForDifferentEvent_DoesNotConflict()
    {
        await Handle(_rivalId, _vipSeats[0].Id);
        var later = Event.Create(_event.VenueId, "Later", EventCategory.Concert, null, Now.AddDays(20), Now.AddDays(20).AddHours(1), Now);
        later.SetSectionPrice(_vipSeats[0].SectionId, 9000, Now);
        _dbContext.Events.Add(later);
        await _dbContext.SaveChangesAsync();

        var response = await Send(new HoldSeatsCommand(later.Id, _customerId, [_vipSeats[0].Id]));

        response.TotalPriceCents.Should().Be(9000);
    }

    private Task<HoldSeatsResponse> Handle(Guid customerId, params Guid[] seatIds) =>
        Send(new HoldSeatsCommand(_event.Id, customerId, seatIds));

    private Task<HoldSeatsResponse> Send(HoldSeatsCommand command) =>
        new HoldSeatsCommandHandler(_dbContext, _customerEventLock.Object, new FixedTimeProvider(TestDbContextFactory.UtcNow))
            .Handle(command, CancellationToken.None);
}
