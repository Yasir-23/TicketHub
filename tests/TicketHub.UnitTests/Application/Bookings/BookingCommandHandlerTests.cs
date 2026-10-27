using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Common.Models;
using TicketHub.Application.Features.Bookings.CancelBooking;
using TicketHub.Application.Features.Bookings.CheckoutBooking;
using TicketHub.Application.Features.Events.GetEventSeatMap;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Bookings;

public sealed class BookingCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTime Now = TestDbContextFactory.UtcNow.UtcDateTime;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly AppDbContext _dbContext;
    private readonly Mock<IPaymentGateway> _paymentGateway = new();
    private readonly Mock<ITicketFulfillmentChannel> _fulfillmentChannel = new();
    private readonly User _customer = User.CreateCustomer("Jane Doe", "jane@example.com", "hash");
    private Guid _customerId => _customer.Id;
    private Event _event = null!;
    private Event _eventUnder48Hours = null!;
    private Event _eventAt48Hours = null!;
    private Seat _seat = null!;

    public BookingCommandHandlerTests() => _dbContext = TestDbContextFactory.Create(_databaseName);

    public async Task InitializeAsync()
    {
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var section = venue.AddSection("VIP", [new SeatRowSpecification("A", 5)]);
        _seat = section.Seats.OrderBy(seat => seat.SeatNumber).First();

        _event = CreateEvent(venue.Id, section.Id, Now.AddDays(10));
        _eventUnder48Hours = CreateEvent(venue.Id, section.Id, Now.AddHours(47));
        _eventAt48Hours = CreateEvent(venue.Id, section.Id, Now.AddHours(48));

        _dbContext.Users.Add(_customer);
        _dbContext.Venues.Add(venue);
        _dbContext.Events.AddRange(_event, _eventUnder48Hours, _eventAt48Hours);
        await _dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => _dbContext.DisposeAsync().AsTask();

    [Fact]
    public async Task Checkout_WithMissingIdempotencyKey_ThrowsMissingKeyException()
    {
        var act = () => Checkout(Guid.NewGuid(), string.Empty);

        await act.Should().ThrowAsync<MissingIdempotencyKeyException>();
    }

    [Fact]
    public async Task Checkout_WithInvalidIdempotencyKey_ThrowsValidationException()
    {
        var act = () => Checkout(Guid.NewGuid(), "not-a-uuid");

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Checkout_WithExpiredHold_ExpiresHoldAndThrowsGoneException()
    {
        var holdId = await CreateHold(_event, Now.AddSeconds(-1));

        var act = () => Checkout(holdId);

        await act.Should().ThrowAsync<HoldExpiredException>();
        (await _dbContext.SeatHolds.SingleAsync()).Status.Should().Be(HoldStatus.Expired);
        _paymentGateway.Verify(gateway => gateway.ChargeAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Checkout_IdempotencyReplayReturnsStoredBookingWithoutChargingAgain()
    {
        var holdId = await CreateHold(_event, Now.AddMinutes(10));
        _paymentGateway
            .Setup(gateway => gateway.ChargeAsync(
                "CreditCard",
                "tok_visa_valid",
                15000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult(PaymentStatus.Success, "sim_success"));
        var key = Guid.NewGuid().ToString();

        var firstResponse = await Checkout(holdId, key);
        var replayedResponse = await Checkout(holdId, key);

        replayedResponse.Should().BeEquivalentTo(firstResponse);
        _paymentGateway.Verify(gateway => gateway.ChargeAsync(
            "CreditCard",
            "tok_visa_valid",
            15000,
            It.IsAny<CancellationToken>()), Times.Once);
        (await _dbContext.Bookings.CountAsync()).Should().Be(1);
        (await _dbContext.PaymentTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Checkout_WhenPaymentDeclined_RecordsFailureAndThrowsPaymentFailed()
    {
        var holdId = await CreateHold(_event, Now.AddMinutes(10));
        _paymentGateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<string>(),
                "tok_fail",
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult(PaymentStatus.Failed, "sim_declined"));

        var act = () => Checkout(holdId, paymentToken: "tok_fail");

        await act.Should().ThrowAsync<PaymentFailedException>();
        (await _dbContext.PaymentTransactions.SingleAsync()).Status.Should().Be(PaymentStatus.Failed);
        (await _dbContext.SeatHolds.SingleAsync()).Status.Should().Be(HoldStatus.Active);
    }

    [Fact]
    public async Task Checkout_AfterSuccessfulPayment_PublishesFulfillmentMessageForCommittedBooking()
    {
        var holdId = await CreateHold(_event, Now.AddMinutes(10));
        SetupSuccessfulPayment();
        TicketFulfillmentMessage? published = null;
        BookingStatus? statusWhenPublished = null;
        _fulfillmentChannel
            .Setup(channel => channel.PublishAsync(It.IsAny<TicketFulfillmentMessage>(), It.IsAny<CancellationToken>()))
            .Callback<TicketFulfillmentMessage, CancellationToken>((message, _) =>
            {
                published = message;
                // Read through a separate context: only committed state is visible there.
                using var verificationContext = TestDbContextFactory.Create(_databaseName);
                statusWhenPublished = verificationContext.Bookings.Single(b => b.Id == message.BookingId).Status;
            })
            .Returns(ValueTask.CompletedTask);

        var response = await Checkout(holdId);

        statusWhenPublished.Should().Be(BookingStatus.Confirmed);
        published.Should().BeEquivalentTo(new TicketFulfillmentMessage(
            response.BookingId,
            _customerId,
            "jane@example.com",
            response.TicketNumber,
            response.QrCodePayload,
            _event.Title,
            _event.StartDateUtc));
    }

    [Fact]
    public async Task Checkout_WhenPaymentDeclined_DoesNotPublishFulfillmentMessage()
    {
        var holdId = await CreateHold(_event, Now.AddMinutes(10));
        _paymentGateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult(PaymentStatus.Failed, "sim_declined"));

        var act = () => Checkout(holdId, paymentToken: "tok_fail");

        await act.Should().ThrowAsync<PaymentFailedException>();
        _fulfillmentChannel.Verify(
            channel => channel.PublishAsync(It.IsAny<TicketFulfillmentMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Checkout_IdempotencyReplay_PublishesFulfillmentMessageOnlyOnce()
    {
        var holdId = await CreateHold(_event, Now.AddMinutes(10));
        SetupSuccessfulPayment();
        var key = Guid.NewGuid().ToString();

        await Checkout(holdId, key);
        await Checkout(holdId, key);

        _fulfillmentChannel.Verify(
            channel => channel.PublishAsync(It.IsAny<TicketFulfillmentMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Checkout_WhenFulfillmentChannelFails_StillReturnsConfirmedBooking()
    {
        var holdId = await CreateHold(_event, Now.AddMinutes(10));
        SetupSuccessfulPayment();
        _fulfillmentChannel
            .Setup(channel => channel.PublishAsync(It.IsAny<TicketFulfillmentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromException(new InvalidOperationException("channel closed")));

        var response = await Checkout(holdId);

        response.Status.Should().Be(nameof(BookingStatus.Confirmed));
        (await _dbContext.Bookings.SingleAsync()).Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task CancelBooking_At48HourCutoffSucceedsAndReleasesSeat()
    {
        var booking = await CreateConfirmedBooking(_eventAt48Hours);
        var handler = new CancelBookingCommandHandler(_dbContext, new FixedTimeProvider(TestDbContextFactory.UtcNow));

        var response = await handler.Handle(new CancelBookingCommand(booking.Id, _customerId), CancellationToken.None);

        response.Status.Should().Be(nameof(BookingStatus.Cancelled));
        response.RefundAmountCents.Should().Be(15000);
        (await _dbContext.SeatHolds.SingleAsync()).Status.Should().Be(HoldStatus.Released);

        var seatMap = await new GetEventSeatMapQueryHandler(
                _dbContext,
                new FixedTimeProvider(TestDbContextFactory.UtcNow))
            .Handle(new GetEventSeatMapQuery(_eventAt48Hours.Id), CancellationToken.None);
        seatMap.Sections.SelectMany(section => section.Seats)
            .Single(seat => seat.SeatId == _seat.Id)
            .Status.Should().Be(SeatStatus.Available);
    }

    [Fact]
    public async Task CancelBooking_Under48HoursThrowsCutoffException()
    {
        var booking = await CreateConfirmedBooking(_eventUnder48Hours);
        var handler = new CancelBookingCommandHandler(_dbContext, new FixedTimeProvider(TestDbContextFactory.UtcNow));

        var act = () => handler.Handle(new CancelBookingCommand(booking.Id, _customerId), CancellationToken.None);

        await act.Should().ThrowAsync<CancellationCutoffPassedException>();
        (await _dbContext.Bookings.SingleAsync(item => item.Id == booking.Id))
            .Status.Should().Be(BookingStatus.Confirmed);
    }

    private void SetupSuccessfulPayment() =>
        _paymentGateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayResult(PaymentStatus.Success, "sim_success"));

    private Event CreateEvent(Guid venueId, Guid sectionId, DateTime startDateUtc)
    {
        var @event = Event.Create(
            venueId,
            "Concert",
            EventCategory.Concert,
            null,
            startDateUtc,
            startDateUtc.AddHours(3),
            Now);
        @event.SetSectionPrice(sectionId, 15000, Now);
        return @event;
    }

    private async Task<Guid> CreateHold(Event @event, DateTime expiresAtUtc)
    {
        var groupId = Guid.NewGuid();
        _dbContext.SeatHolds.Add(SeatHold.Create(
            @event.Id,
            _seat.Id,
            _customerId,
            expiresAtUtc,
            groupId));
        await _dbContext.SaveChangesAsync();
        return groupId;
    }

    private async Task<Booking> CreateConfirmedBooking(Event @event)
    {
        var booking = Booking.Create(@event.Id, _customerId, 15000, $"TKT-{Guid.NewGuid():N}");
        booking.AddBookingItem(_seat.Id, 15000);
        booking.Confirm(Now);
        var hold = SeatHold.Create(
            @event.Id,
            _seat.Id,
            _customerId,
            Now.AddMinutes(10),
            Guid.NewGuid());
        hold.ConvertToBooking(booking.Id);
        _dbContext.Bookings.Add(booking);
        _dbContext.SeatHolds.Add(hold);
        await _dbContext.SaveChangesAsync();
        return booking;
    }

    private Task<TicketHub.Application.Features.Bookings.CheckoutBooking.CheckoutBookingResponse> Checkout(
        Guid holdId,
        string? idempotencyKey = null,
        string paymentToken = "tok_visa_valid") =>
        new CheckoutBookingCommandHandler(
                _dbContext,
                _paymentGateway.Object,
                _fulfillmentChannel.Object,
                new FixedTimeProvider(TestDbContextFactory.UtcNow),
                NullLogger<CheckoutBookingCommandHandler>.Instance)
            .Handle(
                new CheckoutBookingCommand(
                    holdId,
                    _customerId,
                    "CreditCard",
                    paymentToken,
                    idempotencyKey ?? Guid.NewGuid().ToString()),
                CancellationToken.None);
}
