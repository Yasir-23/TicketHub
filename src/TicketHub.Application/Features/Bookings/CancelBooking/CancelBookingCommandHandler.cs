using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Application.Features.Bookings.CancelBooking;

public sealed class CancelBookingCommandHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<CancelBookingCommand, CancelBookingResponse>
{
    private static readonly TimeSpan CancellationWindow = TimeSpan.FromHours(48);

    public async Task<CancelBookingResponse> Handle(
        CancelBookingCommand request,
        CancellationToken cancellationToken)
    {
        var booking = await dbContext.Bookings
                .Include(item => item.Event)
                .SingleOrDefaultAsync(item => item.Id == request.BookingId, cancellationToken)
            ?? throw new NotFoundException(nameof(Booking), request.BookingId);

        if (booking.CustomerId != request.CustomerId)
        {
            throw new NotFoundException(nameof(Booking), request.BookingId);
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            return ToResponse(booking);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (booking.Event.StartDateUtc - now < CancellationWindow)
        {
            throw new CancellationCutoffPassedException();
        }

        booking.Cancel(now);
        var holds = await dbContext.SeatHolds
            .Where(hold => hold.BookingId == booking.Id)
            .ToListAsync(cancellationToken);
        foreach (var hold in holds)
        {
            hold.ReleaseBooking();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(booking);
    }

    private static CancelBookingResponse ToResponse(Booking booking) =>
        new(
            booking.Id,
            BookingStatus.Cancelled.ToString(),
            booking.TotalAmountCents,
            new DateTimeOffset(DateTime.SpecifyKind(
                booking.CancelledAtUtc ?? booking.CreatedAtUtc,
                DateTimeKind.Utc)));
}
