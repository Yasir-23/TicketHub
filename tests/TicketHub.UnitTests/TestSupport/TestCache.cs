using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketHub.Infrastructure.Caching;

namespace TicketHub.UnitTests.TestSupport;

internal static class TestCache
{
    /// <summary>The production cache service over an in-process <see cref="IDistributedCache"/>.</summary>
    public static RedisCacheService Create() => Create(CreateDistributedCache());

    public static RedisCacheService Create(IDistributedCache distributedCache) =>
        new(distributedCache, NullLogger<RedisCacheService>.Instance);

    public static IDistributedCache CreateDistributedCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
}
