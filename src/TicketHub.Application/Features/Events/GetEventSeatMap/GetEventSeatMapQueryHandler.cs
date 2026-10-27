using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Caching;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Events.GetEventSeatMap;

public sealed class GetEventSeatMapQueryHandler(IAppDbContext dbContext, ICacheService cache, TimeProvider timeProvider)
    : IRequestHandler<GetEventSeatMapQuery, EventSeatMapResponse>
{
    public Task<EventSeatMapResponse> Handle(GetEventSeatMapQuery request, CancellationToken cancellationToken) =>
        cache.GetOrSetAsync(
            CacheKeys.SeatMap(request.EventId),
            () => LoadSeatMapAsync(request, cancellationToken),
            CacheKeys.SeatMapTtl,
            cancellationToken);

    private async Task<EventSeatMapResponse> LoadSeatMapAsync(GetEventSeatMapQuery request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Events.AnyAsync(e => e.Id == request.EventId, cancellationToken))
        {
            throw new NotFoundException(nameof(Event), request.EventId);
        }

        // Only sections priced for this event are on sale, so only those appear on the map.
        var sections = await dbContext.EventSectionPricings
            .AsNoTracking()
            .Where(p => p.EventId == request.EventId)
            .Select(p => new { p.SectionId, SectionName = p.Section.Name, p.PriceCents })
            .ToListAsync(cancellationToken);

        var sectionIds = sections.Select(s => s.SectionId).ToList();
        var seats = await dbContext.Seats
            .AsNoTracking()
            .Where(s => sectionIds.Contains(s.SectionId))
            .Select(s => new { s.Id, s.SectionId, s.RowNumber, s.SeatNumber })
            .ToListAsync(cancellationToken);

        var seatStatuses = await LoadSeatStatusesAsync(request.EventId, cancellationToken);
        var seatsBySection = seats
            .Select(s => new
            {
                s.SectionId,
                Seat = new SeatMapSeatDto(
                    s.Id,
                    s.RowNumber,
                    s.SeatNumber,
                    seatStatuses.GetValueOrDefault(s.Id, SeatStatus.Available))
            })
            .ToLookup(s => s.SectionId, s => s.Seat);

        var sectionDtos = sections
            .OrderBy(s => s.SectionName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SeatMapSectionDto(
                s.SectionId,
                s.SectionName,
                s.PriceCents,
                seatsBySection[s.SectionId]
                    // Row "B" sorts before "AA" (shorter labels first), matching physical layout.
                    .OrderBy(seat => seat.Row.Length)
                    .ThenBy(seat => seat.Row, StringComparer.Ordinal)
                    .ThenBy(seat => seat.Number)
                    .ToList()))
            .ToList();

        return new EventSeatMapResponse(request.EventId, sectionDtos);
    }

    /// <summary>
    /// Seat status is per event: Booked if a hold was converted to a booking, Held if a hold is still live,
    /// otherwise Available (expired-but-unreaped holds count as Available, per BR-04).
    /// </summary>
    private async Task<Dictionary<Guid, SeatStatus>> LoadSeatStatusesAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var occupied = await dbContext.SeatHolds
            .AsNoTracking()
            .Where(h => h.EventId == eventId
                && (h.Status == HoldStatus.ConvertedToBooking
                    || (h.Status == HoldStatus.Active && h.HoldExpiresAtUtc > utcNow)))
            .Select(h => new { h.SeatId, h.Status })
            .ToListAsync(cancellationToken);

        return occupied
            .GroupBy(h => h.SeatId)
            .ToDictionary(
                group => group.Key,
                group => group.Any(h => h.Status == HoldStatus.ConvertedToBooking) ? SeatStatus.Booked : SeatStatus.Held);
    }
}
