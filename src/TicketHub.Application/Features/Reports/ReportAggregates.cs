using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Reports;

/// <summary>
/// Shared read-only aggregates for executive reports. Revenue counts only Confirmed bookings: cancelled bookings
/// were refunded and pending ones were never paid.
/// </summary>
internal static class ReportAggregates
{
    /// <summary>
    /// Built with an object initializer, not a constructor, so EF can translate ordering and paging over it.
    /// </summary>
    internal sealed class EventTotals
    {
        public Guid EventId { get; init; }

        public long RevenueCents { get; init; }

        public int BookingCount { get; init; }
    }

    internal sealed record EventInfo(Guid Id, string Title, EventCategory Category, DateTime StartDateUtc);

    /// <summary>Revenue and booking count per event, computed in the database.</summary>
    public static IQueryable<EventTotals> ConfirmedTotalsByEvent(IAppDbContext dbContext) =>
        dbContext.Bookings
            .AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.Confirmed)
            .GroupBy(booking => booking.EventId)
            .Select(group => new EventTotals
            {
                EventId = group.Key,
                RevenueCents = group.Sum(b => b.TotalAmountCents),
                BookingCount = group.Count()
            });

    /// <summary>Tickets (seats) sold per event on confirmed bookings.</summary>
    public static async Task<Dictionary<Guid, int>> ConfirmedTicketsByEventAsync(
        IAppDbContext dbContext,
        IReadOnlyCollection<Guid>? eventIds,
        CancellationToken cancellationToken)
    {
        var items = dbContext.BookingItems
            .AsNoTracking()
            .Where(item => item.Booking.Status == BookingStatus.Confirmed);

        if (eventIds is not null)
        {
            items = items.Where(item => eventIds.Contains(item.Booking.EventId));
        }

        return await items
            .GroupBy(item => item.Booking.EventId)
            .Select(group => new { EventId = group.Key, Tickets = group.Count() })
            .ToDictionaryAsync(row => row.EventId, row => row.Tickets, cancellationToken);
    }

    /// <summary>
    /// Event details for report rows. Soft-deleted events are included: money they earned is still revenue.
    /// </summary>
    public static Task<Dictionary<Guid, EventInfo>> LoadEventsAsync(
        IAppDbContext dbContext,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken cancellationToken) =>
        dbContext.Events
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => eventIds.Contains(e.Id))
            .Select(e => new EventInfo(e.Id, e.Title, e.Category, e.StartDateUtc))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
}
