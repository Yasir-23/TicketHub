using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Caching;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Common.Models;
using TicketHub.Domain.Common;

namespace TicketHub.Application.Features.Events.GetEvents;

public sealed class GetEventsQueryHandler(
    IAppDbContext dbContext,
    ICacheService cache,
    ICatalogCacheGeneration catalogGeneration)
    : IRequestHandler<GetEventsQuery, PagedResult<EventSummaryDto>>
{
    public async Task<PagedResult<EventSummaryDto>> Handle(GetEventsQuery request, CancellationToken cancellationToken)
    {
        var generation = await catalogGeneration.GetCurrentAsync(cancellationToken);

        return await cache.GetOrSetAsync(
            CacheKeys.CatalogPage(generation, request),
            () => LoadPageAsync(request, cancellationToken),
            CacheKeys.CatalogTtl,
            cancellationToken);
    }

    private async Task<PagedResult<EventSummaryDto>> LoadPageAsync(GetEventsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.Events.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(term) || e.Description.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim().ToLower();
            query = query.Where(e => e.Venue.City.ToLower() == city);
        }

        if (request.Category is { } category)
        {
            query = query.Where(e => e.Category == category);
        }

        if (request.StartDateFrom is { } from)
        {
            var fromUtc = from.AsUtc();
            query = query.Where(e => e.StartDateUtc >= fromUtc);
        }

        if (request.StartDateTo is { } to)
        {
            var toUtc = to.AsUtc();
            query = query.Where(e => e.StartDateUtc <= toUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(e => e.StartDateUtc)
            .ThenBy(e => e.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new EventSummaryDto(
                e.Id,
                e.Title,
                e.Category,
                e.Venue.Name,
                e.Venue.City,
                e.StartDateUtc,
                e.SectionPricings.Min(p => (long?)p.PriceCents)))
            .ToListAsync(cancellationToken);

        return PagedResult<EventSummaryDto>.Create(items, request.PageNumber, request.PageSize, totalCount);
    }
}
