using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs <see cref="RunOnceAsync"/> at startup and then every <see cref="Interval"/>. A failed run is logged and
/// retried on the next tick, so one bad cycle never stops the host.
/// </summary>
public abstract class PeriodicBackgroundWorker(TimeProvider timeProvider, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerName = GetType().Name;
        logger.LogInformation("{WorkerName} started; running every {Interval}", workerName, Interval);

        using var timer = new PeriodicTimer(Interval, timeProvider);
        try
        {
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "{WorkerName} run failed; retrying next interval", workerName);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown; the in-progress run was cancelled before committing.
        }

        logger.LogInformation("{WorkerName} stopped", workerName);
    }
}
