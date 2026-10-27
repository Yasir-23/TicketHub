using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Application.Features.Reports.GetTopGrossingEvents;

public sealed class GetTopGrossingEventsQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetTopGrossingEventsQuery, IReadOnlyList<TopGrossingEventDto>>
{
    public async Task<IReadOnlyList<TopGrossingEventDto>> Handle(
        GetTopGrossingEventsQuery request,
        CancellationToken cancellationToken)
    {
        // Ranked and limited in the database; ties broken by booking count, then id, so ranks are stable.
        var top = await ReportAggregates.ConfirmedTotalsByEvent(dbContext)
            .OrderByDescending(t => t.RevenueCents)
            .ThenByDescending(t => t.BookingCount)
            .ThenBy(t => t.EventId)
            .Take(GetTopGrossingEventsQuery.Limit)
            .ToListAsync(cancellationToken);

        var eventIds = top.Select(t => t.EventId).ToList();
        var tickets = await ReportAggregates.ConfirmedTicketsByEventAsync(dbContext, eventIds, cancellationToken);
        var events = await ReportAggregates.LoadEventsAsync(dbContext, eventIds, cancellationToken);

        return top
            .Select((t, index) => new TopGrossingEventDto(
                index + 1,
                t.EventId,
                events[t.EventId].Title,
                events[t.EventId].Category,
                events[t.EventId].StartDateUtc,
                t.RevenueCents,
                tickets.GetValueOrDefault(t.EventId),
                t.BookingCount))
            .ToList();
    }
}
