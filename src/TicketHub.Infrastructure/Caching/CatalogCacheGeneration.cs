using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TicketHub.Application.Common.Caching;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Infrastructure.Caching;

/// <summary>
/// Generation counter stored in Redis and advanced with INCR, so concurrent admin changes never lose an
/// increment across API instances. If Redis is unreachable, reads fall back to generation 0 and a failed
/// increment is logged: the committed change still appears once the cached pages' TTL lapses.
/// </summary>
public sealed class RedisCatalogCacheGeneration(
    IConnectionMultiplexer redis,
    ILogger<RedisCatalogCacheGeneration> logger) : ICatalogCacheGeneration
{
    public async Task<long> GetCurrentAsync(CancellationToken ct = default)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(CacheKeys.CatalogGenerationKey);
            return value.TryParse(out long generation) ? generation : 0;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            logger.LogWarning(exception, "Could not read the catalog cache generation; using 0");
            return 0;
        }
    }

    public async Task<long> IncrementAsync(CancellationToken ct = default)
    {
        try
        {
            var generation = await redis.GetDatabase().StringIncrementAsync(CacheKeys.CatalogGenerationKey);
            logger.LogInformation("Catalog cache invalidated; generation is now {CatalogGeneration}", generation);
            return generation;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            logger.LogError(
                exception,
                "Could not advance the catalog cache generation; cached catalog pages may be stale for up to {CatalogTtl}",
                CacheKeys.CatalogTtl);
            return 0;
        }
    }
}

/// <summary>Single-process generation counter, used when no Redis connection is configured.</summary>
public sealed class InMemoryCatalogCacheGeneration : ICatalogCacheGeneration
{
    private long _generation;

    public Task<long> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(Interlocked.Read(ref _generation));

    public Task<long> IncrementAsync(CancellationToken ct = default) => Task.FromResult(Interlocked.Increment(ref _generation));
}
