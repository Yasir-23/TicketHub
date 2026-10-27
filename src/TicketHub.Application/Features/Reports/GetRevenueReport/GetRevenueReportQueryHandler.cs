using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Application.Features.Reports.GetRevenueReport;

public sealed class GetRevenueReportQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetRevenueReportQuery, RevenueReportResponse>
{
    public async Task<RevenueReportResponse> Handle(GetRevenueReportQuery request, CancellationToken cancellationToken)
    {
        var totals = await ReportAggregates.ConfirmedTotalsByEvent(dbContext).ToListAsync(cancellationToken);
        var tickets = await ReportAggregates.ConfirmedTicketsByEventAsync(dbContext, eventIds: null, cancellationToken);
        var events = await ReportAggregates.LoadEventsAsync(
            dbContext,
            totals.Select(t => t.EventId).ToList(),
            cancellationToken);

        var eventRows = totals
            .Select(t => new
            {
                events[t.EventId].Category,
                Row = new EventRevenueDto(
                    t.EventId,
                    events[t.EventId].Title,
                    events[t.EventId].StartDateUtc,
                    t.RevenueCents,
                    tickets.GetValueOrDefault(t.EventId),
                    t.BookingCount)
            })
            .ToList();

        var categories = eventRows
            .GroupBy(e => e.Category)
            .Select(group => new CategoryRevenueDto(
                group.Key,
                group.Sum(e => e.Row.RevenueCents),
                group.Sum(e => e.Row.TicketsSold),
                group.Sum(e => e.Row.BookingCount),
                group.Select(e => e.Row)
                    .OrderByDescending(e => e.RevenueCents)
                    .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderByDescending(c => c.RevenueCents)
            .ThenBy(c => c.Category)
            .ToList();

        return new RevenueReportResponse(
            categories.Sum(c => c.RevenueCents),
            categories.Sum(c => c.TicketsSold),
            categories.Sum(c => c.BookingCount),
            categories);
    }
}
