using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Common;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Application.Features.Holds.HoldSeats;

/// <summary>
/// The hot path (SDD section 4). Concurrency guarantees:
/// <list type="bullet">
/// <item>BR-01: the filtered unique index UX_SeatHolds_Active rejects a second active hold on a seat; the
/// persistence layer reports that as <see cref="SeatAlreadyHeldException"/>, rolling back the whole batch.</item>
/// <item>BR-02: <see cref="ICustomerEventLock"/> serializes one customer's requests for one event, so the
/// count-then-insert below cannot be raced.</item>
/// </list>
/// </summary>
public sealed class HoldSeatsCommandHandler(
    IAppDbContext dbContext,
    ICustomerEventLock customerEventLock,
    TimeProvider timeProvider)
    : IRequestHandler<HoldSeatsCommand, HoldSeatsResponse>
{
    public async Task<HoldSeatsResponse> Handle(HoldSeatsCommand request, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var @event = await dbContext.Events
                .AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), request.EventId);

        @event.EnsureBookable(utcNow);

        var seats = await LoadSellableSeatsAsync(request, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await customerEventLock.AcquireAsync(request.CustomerId, request.EventId, cancellationToken);

        await EnsureWithinTicketLimitAsync(request, utcNow, cancellationToken);
        await ReleaseStaleHoldsOrThrowIfTakenAsync(request, utcNow, cancellationToken);

        var holdGroupId = Guid.NewGuid();
        var expiresAtUtc = utcNow.Add(BookingRules.HoldDuration);

        dbContext.SeatHolds.AddRange(seats.Select(seat =>
            SeatHold.Create(request.EventId, seat.SeatId, request.CustomerId, expiresAtUtc, holdGroupId)));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new HoldSeatsResponse(
            holdGroupId,
            request.EventId,
            seats,
            seats.Sum(seat => seat.PriceCents),
            expiresAtUtc);
    }

    /// <summary>Seats must exist and belong to a section that is priced (on sale) for this event.</summary>
    private async Task<List<HeldSeatDto>> LoadSellableSeatsAsync(HoldSeatsCommand request, CancellationToken cancellationToken)
    {
        var seatIds = request.SeatIds;

        var seats = await (
                from seat in dbContext.Seats
                join pricing in dbContext.EventSectionPricings on seat.SectionId equals pricing.SectionId
                where pricing.EventId == request.EventId && seatIds.Contains(seat.Id)
                select new HeldSeatDto(seat.Id, seat.RowNumber, seat.SeatNumber, pricing.PriceCents))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var unknownSeatIds = seatIds.Except(seats.Select(s => s.SeatId)).ToList();
        if (unknownSeatIds.Count != 0)
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(HoldSeatsCommand.SeatIds),
                    $"Seats are not on sale for event '{request.EventId}': {string.Join(", ", unknownSeatIds)}.")
            ]);
        }

        return seats
            .OrderBy(s => s.Row.Length)
            .ThenBy(s => s.Row, StringComparer.Ordinal)
            .ThenBy(s => s.SeatNumber)
            .ToList();
    }

    /// <summary>BR-02: live holds plus seats already converted to bookings count towards the limit.</summary>
    private async Task EnsureWithinTicketLimitAsync(HoldSeatsCommand request, DateTime utcNow, CancellationToken cancellationToken)
    {
        var currentCount = await dbContext.SeatHolds.CountAsync(
            h => h.EventId == request.EventId
                && h.CustomerId == request.CustomerId
                && ((h.Status == HoldStatus.Active && h.HoldExpiresAtUtc > utcNow)
                    || h.Status == HoldStatus.ConvertedToBooking),
            cancellationToken);

        if (currentCount + request.SeatIds.Count > BookingRules.MaxTicketsPerCustomerPerEvent)
        {
            throw new TicketLimitExceededException(
                BookingRules.MaxTicketsPerCustomerPerEvent,
                currentCount,
                request.SeatIds.Count);
        }
    }

    /// <summary>
    /// A live hold or a booking makes a seat unavailable. Holds past their expiry but not yet reaped still
    /// occupy the unique index, so they are expired here to make the seat immediately reusable (BR-04).
    /// </summary>
    private async Task ReleaseStaleHoldsOrThrowIfTakenAsync(
        HoldSeatsCommand request,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var seatIds = request.SeatIds;

        var existingHolds = await dbContext.SeatHolds
            .Where(h => h.EventId == request.EventId
                && seatIds.Contains(h.SeatId)
                && (h.Status == HoldStatus.Active || h.Status == HoldStatus.ConvertedToBooking))
            .ToListAsync(cancellationToken);

        var takenSeatIds = existingHolds
            .Where(h => h.Status == HoldStatus.ConvertedToBooking || h.IsActiveAt(utcNow))
            .Select(h => h.SeatId)
            .Distinct()
            .ToList();

        if (takenSeatIds.Count != 0)
        {
            throw new SeatAlreadyHeldException(takenSeatIds);
        }

        if (existingHolds.Count == 0)
        {
            return;
        }

        foreach (var staleHold in existingHolds)
        {
            staleHold.Expire();
        }

        // Saved separately so the index slot is free before the new holds are inserted.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
