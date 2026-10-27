namespace TicketHub.Application.Common.Interfaces;

/// <summary>
/// Distributed cache for read models (SDD section 7). Implementations treat the cache as an optimization:
/// when it is unreachable, reads miss and writes are skipped rather than failing the request.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Returns the cached value, or runs <paramref name="factory"/> and caches its non-null result.</summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken ct = default);
}
