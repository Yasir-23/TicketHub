using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Events.GetEventSeatMap;

public sealed class GetEventSeatMapQueryHandler(IAppDbContext dbContext)
    : IRequestHandler<GetEventSeatMapQuery, EventSeatMapResponse>
{
    public async Task<EventSeatMapResponse> Handle(GetEventSeatMapQuery request, CancellationToken cancellationToken)
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
        var seatsBySection = (await dbContext.Seats
                .AsNoTracking()
                .Where(s => sectionIds.Contains(s.SectionId))
                .Select(s => new { s.SectionId, Seat = new SeatMapSeatDto(s.Id, s.RowNumber, s.SeatNumber, s.Status) })
                .ToListAsync(cancellationToken))
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
}
