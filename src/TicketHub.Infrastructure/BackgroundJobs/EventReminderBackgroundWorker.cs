using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// FR-JOB-03: every <see cref="EventReminderOptions.Interval"/> (hourly by default), dispatches 24-hour reminders
/// for upcoming events, in batches, each batch in its own scope.
/// </summary>
public sealed class EventReminderBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EventReminderOptions> options,
    TimeProvider timeProvider,
    ILogger<EventReminderBackgroundWorker> logger) : PeriodicBackgroundWorker(timeProvider, logger)
{
    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.BatchSize;
        var total = 0;
        int dispatched;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<EventReminderDispatcher>();
            dispatched = await dispatcher.DispatchDueRemindersAsync(cancellationToken);
            total += dispatched;
        }
        while (dispatched == batchSize);

        if (total > 0)
        {
            logger.LogInformation("Dispatched {ReminderCount} event reminders", total);
        }
    }
}
