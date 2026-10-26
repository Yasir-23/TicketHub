using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Infrastructure.Persistence;

/// <summary>
/// Uses a transaction-scoped PostgreSQL advisory lock, released automatically on commit or rollback.
/// Must be called inside an open transaction on the same <see cref="AppDbContext"/>.
/// </summary>
internal sealed class PostgresAdvisoryCustomerEventLock(AppDbContext dbContext) : ICustomerEventLock
{
    public async Task AcquireAsync(Guid customerId, Guid eventId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Advisory locks must be acquired inside a transaction.");
        }

        var key = CreateLockKey(customerId, eventId);
        await dbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    // Hash collisions only cause unrelated requests to briefly wait for each other; they never break correctness.
    internal static long CreateLockKey(Guid customerId, Guid eventId)
    {
        Span<byte> customer = stackalloc byte[16];
        Span<byte> @event = stackalloc byte[16];
        customerId.TryWriteBytes(customer);
        eventId.TryWriteBytes(@event);

        return BitConverter.ToInt64(customer[..8]) ^ BitConverter.ToInt64(customer[8..])
            ^ BitConverter.ToInt64(@event[..8]) * 31 ^ BitConverter.ToInt64(@event[8..]);
    }
}
