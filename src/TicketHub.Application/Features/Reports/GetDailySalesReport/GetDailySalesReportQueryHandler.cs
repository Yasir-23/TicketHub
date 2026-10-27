using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Reports.GetDailySalesReport;

/// <summary>
/// Groups confirmed bookings by the UTC day they were confirmed. Bookings later cancelled are excluded,
/// consistent with the revenue report.
/// </summary>
public sealed class GetDailySalesReportQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetDailySalesReportQuery, DailySalesReportResponse>
{
    public async Task<DailySalesReportResponse> Handle(
        GetDailySalesReportQuery request,
        CancellationToken cancellationToken)
    {
        // The validator guarantees both bounds are present and ordered.
        var from = request.From!.Value;
        var to = request.To!.Value;
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusiveUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var bookingsByDay = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.Confirmed
                && b.ConfirmedAtUtc >= fromUtc
                && b.ConfirmedAtUtc < toExclusiveUtc)
            .GroupBy(b => b.ConfirmedAtUtc!.Value.Date)
            .Select(group => new { Day = group.Key, Bookings = group.Count(), Revenue = group.Sum(b => b.TotalAmountCents) })
            .ToDictionaryAsync(row => DateOnly.FromDateTime(row.Day), cancellationToken);

        var ticketsByDay = await dbContext.BookingItems
            .AsNoTracking()
            .Where(item => item.Booking.Status == BookingStatus.Confirmed
                && item.Booking.ConfirmedAtUtc >= fromUtc
                && item.Booking.ConfirmedAtUtc < toExclusiveUtc)
            .GroupBy(item => item.Booking.ConfirmedAtUtc!.Value.Date)
            .Select(group => new { Day = group.Key, Tickets = group.Count() })
            .ToDictionaryAsync(row => DateOnly.FromDateTime(row.Day), row => row.Tickets, cancellationToken);

        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => from.AddDays(offset))
            .Select(day => bookingsByDay.TryGetValue(day, out var sales)
                ? new DailySalesDto(day, sales.Bookings, ticketsByDay.GetValueOrDefault(day), sales.Revenue)
                : new DailySalesDto(day, 0, 0, 0))
            .ToList();

        return new DailySalesReportResponse(
            from,
            to,
            days.Sum(d => d.RevenueCents),
            days.Sum(d => d.BookingCount),
            days.Sum(d => d.TicketsSold),
            days);
    }
}
