using System.ComponentModel.DataAnnotations;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>FR-JOB-01 / SDD section 6.1.</summary>
public sealed class ExpiredHoldReaperOptions
{
    public const string SectionName = "BackgroundJobs:ExpiredHoldReaper";

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 100;
}

/// <summary>FR-JOB-02 / SDD section 6.2. The delays stand in for real rendering and SMTP round trips.</summary>
public sealed class TicketFulfillmentOptions
{
    public const string SectionName = "BackgroundJobs:TicketFulfillment";

    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan SimulatedRenderDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan SimulatedEmailDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// FR-JOB-03. Each run reminds bookings for events starting in [now + LeadTime, now + LeadTime + Window).
/// Window should be at least Interval so consecutive runs leave no gap; overlap is harmless because each
/// booking is reminded only once.
/// </summary>
public sealed class EventReminderOptions
{
    public const string SectionName = "BackgroundJobs:EventReminder";

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    [Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
    public TimeSpan LeadTime { get; set; } = TimeSpan.FromHours(24);

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);

    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 500;
}
