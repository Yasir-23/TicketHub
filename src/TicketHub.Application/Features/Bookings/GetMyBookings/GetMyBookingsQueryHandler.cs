using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Bookings.GetMyBookings;

public sealed class GetMyBookingsQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetMyBookingsQuery, IReadOnlyList<MyBookingResponse>>
{
    public async Task<IReadOnlyList<MyBookingResponse>> Handle(
        GetMyBookingsQuery request,
        CancellationToken cancellationToken)
    {
        var bookings = await dbContext.Bookings
            .AsNoTracking()
            .Include(booking => booking.Event)
            .Include(booking => booking.BookingItems)
                .ThenInclude(item => item.Seat)
            .Where(booking =>
                booking.CustomerId == request.CustomerId
                && booking.Status != BookingStatus.PendingPayment)
            .OrderByDescending(booking => booking.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return bookings.Select(booking => new MyBookingResponse(
            booking.Id,
            booking.EventId,
            booking.Event.Title,
            AsUtcOffset(booking.Event.StartDateUtc),
            booking.Status.ToString(),
            booking.TotalAmountCents,
            booking.TicketNumber,
            booking.QrCodePayload,
            AsUtcOffset(booking.CreatedAtUtc),
            booking.CancelledAtUtc is null ? null : AsUtcOffset(booking.CancelledAtUtc.Value),
            booking.BookingItems
                .OrderBy(item => item.Seat.RowNumber, StringComparer.Ordinal)
                .ThenBy(item => item.Seat.SeatNumber)
                .Select(item => new MyBookingItemResponse(
                    item.SeatId,
                    item.Seat.RowNumber,
                    item.Seat.SeatNumber,
                    item.UnitPriceCents))
                .ToList()))
            .ToList();
    }

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
