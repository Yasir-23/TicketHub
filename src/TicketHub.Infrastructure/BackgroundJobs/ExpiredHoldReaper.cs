using Microsoft.EntityFrameworkCore;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// One reaper batch (FR-JOB-01, SDD section 6.1): marks up to <c>batchSize</c> lapsed active holds as Expired.
/// Seat availability is derived per event from holds, so an expired hold frees its seat on the seat map and
/// removes it from the UX_SeatHolds_Active index, letting the seat be held again.
/// </summary>
public sealed class ExpiredHoldReaper(AppDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<int> ReapBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var expiredHolds = await LoadExpiredHoldsAsync(utcNow, batchSize, cancellationToken);
        foreach (var hold in expiredHolds)
        {
            hold.Expire();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return expiredHolds.Count;
    }

    private Task<List<SeatHold>> LoadExpiredHoldsAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return dbContext.SeatHolds
                .Where(hold => hold.Status == HoldStatus.Active && hold.HoldExpiresAtUtc <= utcNow)
                .OrderBy(hold => hold.HoldExpiresAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        // SKIP LOCKED: when several API instances run the reaper, each claims a disjoint batch instead of
        // blocking on (or double-processing) rows another instance is already expiring.
        return dbContext.SeatHolds
            .FromSql($"""
                SELECT * FROM "SeatHolds"
                WHERE "Status" = {(int)HoldStatus.Active} AND "HoldExpiresAtUtc" <= {utcNow}
                ORDER BY "HoldExpiresAtUtc"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
    }
}
