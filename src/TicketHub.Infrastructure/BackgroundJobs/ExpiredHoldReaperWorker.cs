using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// FR-JOB-01: every <see cref="ExpiredHoldReaperOptions.Interval"/> (15 s by default), expires lapsed seat holds
/// in batches of <see cref="ExpiredHoldReaperOptions.BatchSize"/>, each batch in its own scope and transaction.
/// </summary>
public sealed class ExpiredHoldReaperWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ExpiredHoldReaperOptions> options,
    TimeProvider timeProvider,
    ILogger<ExpiredHoldReaperWorker> logger) : PeriodicBackgroundWorker(timeProvider, logger)
{
    protected override TimeSpan Interval => options.Value.Interval;

    protected override Task RunOnceAsync(CancellationToken cancellationToken) => ReapAllAsync(cancellationToken);

    /// <summary>Reaps batch after batch until one comes back short, so a backlog clears within a single cycle.</summary>
    internal async Task<int> ReapAllAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.BatchSize;
        var total = 0;
        int reaped;

        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reaper = scope.ServiceProvider.GetRequiredService<ExpiredHoldReaper>();
            reaped = await reaper.ReapBatchAsync(batchSize, cancellationToken);
            total += reaped;
        }
        while (reaped == batchSize);

        if (total > 0)
        {
            logger.LogInformation("Expired {ExpiredHoldCount} lapsed seat holds", total);
        }

        return total;
    }
}
