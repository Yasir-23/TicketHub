using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Reports.GetEventOccupancyReport;

/// <summary>
/// Total seats are those in sections priced for the event, i.e. on sale, matching the seat map, so
/// Sold + Held + Available always equals Total. Sold seats are on confirmed bookings; held seats have an active,
/// unexpired hold (a lapsed hold the reaper has not processed yet already counts as Available, per BR-04).
/// </summary>
public sealed class GetEventOccupancyReportQueryHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetEventOccupancyReportQuery, EventOccupancyReportResponse>
{
    public async Task<EventOccupancyReportResponse> Handle(
        GetEventOccupancyReportQuery request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var @event = await dbContext.Events
                .AsNoTracking()
                .Where(e => e.Id == request.EventId)
                .Select(e => new { e.Id, e.Title })
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Event), request.EventId);

        var seatCountsBySection = await dbContext.EventSectionPricings
            .AsNoTracking()
            .Where(p => p.EventId == request.EventId)
            .Select(p => new
            {
                p.SectionId,
                SectionName = p.Section.Name,
                TotalSeats = dbContext.Seats.Count(s => s.SectionId == p.SectionId)
            })
            .ToListAsync(cancellationToken);

        var soldSeats = await dbContext.BookingItems
            .AsNoTracking()
            .Where(item => item.Booking.EventId == request.EventId && item.Booking.Status == BookingStatus.Confirmed)
            .Select(item => new { item.SeatId, item.Seat.SectionId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var heldSeats = await dbContext.SeatHolds
            .AsNoTracking()
            .Where(h => h.EventId == request.EventId
                && h.Status == HoldStatus.Active
                && h.HoldExpiresAtUtc > utcNow)
            .Select(h => new { h.SeatId, h.Seat.SectionId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var soldSeatIds = soldSeats.Select(s => s.SeatId).ToHashSet();
        var soldBySection = soldSeats.GroupBy(s => s.SectionId).ToDictionary(g => g.Key, g => g.Count());
        // A seat is counted once: if it is somehow both sold and held, sold wins (as on the seat map).
        var heldBySection = heldSeats
            .Where(h => !soldSeatIds.Contains(h.SeatId))
            .GroupBy(h => h.SectionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var sections = seatCountsBySection
            .Select(s => CreateSection(
                s.SectionId,
                s.SectionName,
                s.TotalSeats,
                soldBySection.GetValueOrDefault(s.SectionId),
                heldBySection.GetValueOrDefault(s.SectionId)))
            .OrderBy(s => s.SectionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalSeats = sections.Sum(s => s.TotalSeats);
        var sold = sections.Sum(s => s.SoldSeats);
        var held = sections.Sum(s => s.HeldSeats);

        return new EventOccupancyReportResponse(
            @event.Id,
            @event.Title,
            totalSeats,
            sold,
            held,
            totalSeats - sold - held,
            Percentage(sold, totalSeats),
            sections);
    }

    private static SectionOccupancyDto CreateSection(Guid sectionId, string name, int total, int sold, int held) =>
        new(sectionId, name, total, sold, held, total - sold - held, Percentage(sold, total));

    internal static decimal Percentage(int part, int whole) =>
        whole == 0 ? 0m : Math.Round(part * 100m / whole, 2, MidpointRounding.AwayFromZero);
}
