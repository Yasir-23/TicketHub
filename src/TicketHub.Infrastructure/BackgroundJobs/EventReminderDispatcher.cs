using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// FR-JOB-03: reminds customers with confirmed bookings for events starting in the reminder window. Each booking
/// is stamped with <see cref="Booking.ReminderSentAtUtc"/> before dispatch, so overlapping windows and restarts
/// never send the same reminder twice (at-most-once delivery).
/// </summary>
public sealed class EventReminderDispatcher(
    AppDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<EventReminderOptions> options,
    ILogger<EventReminderDispatcher> logger)
{
    public async Task<int> DispatchDueRemindersAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var dueBookings = await DueForReminder(dbContext.Bookings, utcNow, settings.LeadTime, settings.Window)
            .Include(booking => booking.Event)
            .Include(booking => booking.User)
            .OrderBy(booking => booking.Event.StartDateUtc)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        if (dueBookings.Count == 0)
        {
            return 0;
        }

        foreach (var booking in dueBookings)
        {
            booking.MarkReminderSent(utcNow);
        }

        // Persist before dispatching: a crash in between loses a reminder rather than repeating one.
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var booking in dueBookings)
        {
            logger.LogInformation(
                "Reminder dispatched for booking {BookingId} ({TicketNumber}): {EventTitle} starts at {StartDateUtc:O}, customer {CustomerId}",
                booking.Id,
                booking.TicketNumber,
                booking.Event.Title,
                booking.Event.StartDateUtc,
                booking.CustomerId);
        }

        return dueBookings.Count;
    }

    /// <summary>
    /// Confirmed, not-yet-reminded bookings whose event starts in [now + leadTime, now + leadTime + window).
    /// Soft-deleted events drop out through the Event query filter.
    /// </summary>
    internal static IQueryable<Booking> DueForReminder(
        IQueryable<Booking> bookings,
        DateTime utcNow,
        TimeSpan leadTime,
        TimeSpan window)
    {
        var windowStartUtc = utcNow + leadTime;
        var windowEndUtc = windowStartUtc + window;

        return bookings.Where(booking =>
            booking.Status == BookingStatus.Confirmed
            && booking.ReminderSentAtUtc == null
            && booking.Event.StartDateUtc >= windowStartUtc
            && booking.Event.StartDateUtc < windowEndUtc);
    }
}
