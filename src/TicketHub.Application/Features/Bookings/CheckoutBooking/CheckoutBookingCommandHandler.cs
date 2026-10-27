using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Common.Models;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Application.Features.Bookings.CheckoutBooking;

public sealed class CheckoutBookingCommandHandler(
    IAppDbContext dbContext,
    IPaymentGateway paymentGateway,
    ITicketFulfillmentChannel fulfillmentChannel,
    TimeProvider timeProvider,
    ILogger<CheckoutBookingCommandHandler> logger)
    : IRequestHandler<CheckoutBookingCommand, CheckoutBookingResponse>
{
    public async Task<CheckoutBookingResponse> Handle(
        CheckoutBookingCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new MissingIdempotencyKeyException();
        }

        if (!Guid.TryParse(request.IdempotencyKey, out _))
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(request.IdempotencyKey), "Idempotency-Key must be a valid UUID.")]);
        }

        var existingTransaction = await FindTransactionAsync(request.IdempotencyKey, cancellationToken);
        if (existingTransaction is not null)
        {
            return ReplayOrThrow(existingTransaction, request.CustomerId);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var holds = await dbContext.SeatHolds
            .Include(hold => hold.Event)
            .Where(hold => hold.HoldGroupId == request.HoldId)
            .ToListAsync(cancellationToken);

        if (holds.Count == 0 || holds.Any(hold => hold.CustomerId != request.CustomerId))
        {
            throw new NotFoundException(nameof(SeatHold), request.HoldId);
        }

        if (holds.Any(hold => hold.Status != HoldStatus.Active || hold.HoldExpiresAtUtc <= now))
        {
            foreach (var expiredHold in holds.Where(hold =>
                         hold.Status == HoldStatus.Active && hold.HoldExpiresAtUtc <= now))
            {
                expiredHold.Expire();
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            throw new HoldExpiredException(request.HoldId);
        }

        var @event = holds[0].Event;
        @event.EnsureBookable(now);
        var seatPrices = await (
                from hold in dbContext.SeatHolds
                join seat in dbContext.Seats on hold.SeatId equals seat.Id
                join pricing in dbContext.EventSectionPricings on new { hold.EventId, seat.SectionId }
                    equals new { pricing.EventId, pricing.SectionId }
                where hold.HoldGroupId == request.HoldId
                select new { hold.SeatId, pricing.PriceCents })
            .ToListAsync(cancellationToken);

        if (seatPrices.Count != holds.Count)
        {
            throw new NotFoundException("Priced seat for hold", request.HoldId);
        }

        var totalAmountCents = seatPrices.Sum(item => item.PriceCents);
        var ticketNumber = CreateTicketNumber(@event.StartDateUtc.Year);
        var booking = Booking.Create(
            @event.Id,
            request.CustomerId,
            totalAmountCents,
            ticketNumber);

        foreach (var item in seatPrices)
        {
            booking.AddBookingItem(item.SeatId, item.PriceCents);
        }

        var paymentTransaction = PaymentTransaction.Initiate(
            booking.Id,
            request.IdempotencyKey,
            totalAmountCents);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            dbContext.Bookings.Add(booking);
            dbContext.PaymentTransactions.Add(paymentTransaction);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        PaymentGatewayResult paymentResult;
        try
        {
            paymentResult = await paymentGateway.ChargeAsync(
                request.PaymentMethod,
                request.PaymentToken,
                totalAmountCents,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            paymentTransaction.Complete(PaymentStatus.TimedOut, "gateway_unavailable");
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new PaymentFailedException();
        }

        paymentTransaction.Complete(paymentResult.Status, paymentResult.ProviderReference);
        if (paymentResult.Status != PaymentStatus.Success)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new PaymentFailedException();
        }

        var confirmedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var hold in holds)
        {
            hold.ConvertToBooking(booking.Id);
        }

        booking.Confirm(confirmedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);

        await PublishFulfillmentAsync(booking, @event, cancellationToken);

        return ToResponse(booking, confirmedAtUtc);
    }

    /// <summary>
    /// FR-JOB-02: ticket rendering and email delivery happen off the request path. The booking is already
    /// committed and paid for, so a fulfillment hand-off failure is logged rather than failing checkout.
    /// </summary>
    private async Task PublishFulfillmentAsync(Booking booking, Event @event, CancellationToken cancellationToken)
    {
        try
        {
            var email = await dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == booking.CustomerId)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);

            await fulfillmentChannel.PublishAsync(
                new TicketFulfillmentMessage(
                    booking.Id,
                    booking.CustomerId,
                    email ?? string.Empty,
                    booking.TicketNumber,
                    booking.QrCodePayload,
                    @event.Title,
                    @event.StartDateUtc),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Booking {BookingId} ({TicketNumber}) was confirmed but could not be queued for ticket fulfillment",
                booking.Id,
                booking.TicketNumber);
        }
    }

    private async Task<PaymentTransaction?> FindTransactionAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        await dbContext.PaymentTransactions
            .Include(transaction => transaction.Booking)
            .SingleOrDefaultAsync(transaction => transaction.IdempotencyKey == idempotencyKey, cancellationToken);

    private static CheckoutBookingResponse ReplayOrThrow(PaymentTransaction transaction, Guid customerId)
    {
        if (transaction.Booking.CustomerId != customerId)
        {
            throw new ConflictException("The Idempotency-Key has already been used.");
        }

        return transaction.Status switch
        {
            PaymentStatus.Success => ToResponse(
                transaction.Booking,
                transaction.Booking.ConfirmedAtUtc ?? transaction.Booking.CreatedAtUtc),
            PaymentStatus.Failed or PaymentStatus.TimedOut => throw new PaymentFailedException(),
            _ => throw new ConflictException("A payment with this Idempotency-Key is still processing.")
        };
    }

    private static CheckoutBookingResponse ToResponse(Booking booking, DateTime confirmedAtUtc) =>
        new(
            booking.Id,
            booking.TicketNumber,
            BookingStatus.Confirmed.ToString(),
            booking.TotalAmountCents,
            new DateTimeOffset(DateTime.SpecifyKind(confirmedAtUtc, DateTimeKind.Utc)),
            booking.QrCodePayload);

    private static string CreateTicketNumber(int eventYear) =>
        $"TKT-{eventYear}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
}
