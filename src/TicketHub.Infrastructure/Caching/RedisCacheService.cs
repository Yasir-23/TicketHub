using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Infrastructure.Caching;

/// <summary>
/// JSON-serialized <see cref="ICacheService"/> over <see cref="IDistributedCache"/> (Redis in production).
/// Cache faults and unreadable entries are logged and treated as misses, so Redis being down slows reads
/// down to the database instead of failing them.
/// </summary>
public sealed class RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger) : ICacheService
{
    public static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        byte[]? payload;
        try
        {
            payload = await cache.GetAsync(key, ct);
        }
        catch (Exception exception) when (IsCacheFault(exception, ct))
        {
            logger.LogWarning(exception, "Cache read failed for {CacheKey}; falling back to source", key);
            return default;
        }

        if (payload is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException exception)
        {
            // Typically a stale entry written by an older contract; drop it so the next write replaces it.
            logger.LogWarning(exception, "Discarding unreadable cache entry {CacheKey}", key);
            await RemoveAsync(key, ct);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        try
        {
            await cache.SetAsync(
                key,
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration },
                ct);
        }
        catch (Exception exception) when (IsCacheFault(exception, ct))
        {
            logger.LogWarning(exception, "Cache write failed for {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception exception) when (IsCacheFault(exception, ct))
        {
            logger.LogWarning(exception, "Cache remove failed for {CacheKey}", key);
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken ct = default)
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory();
        if (value is not null)
        {
            await SetAsync(key, value, expiration, ct);
        }

        return value;
    }

    // Caller cancellation still propagates; everything else from the cache backend is a fault to degrade past.
    private static bool IsCacheFault(Exception exception, CancellationToken ct) =>
        !(exception is OperationCanceledException && ct.IsCancellationRequested);
}
