namespace TicketHub.Application.Common.Interfaces;

/// <summary>
/// The tokenized generation counter behind catalog cache keys (SDD section 7). Every catalog key embeds the
/// current generation, so incrementing it orphans all cached catalog pages at once; they then age out by TTL.
/// </summary>
public interface ICatalogCacheGeneration
{
    Task<long> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Atomically advances the generation. Call after the catalog change is committed.</summary>
    Task<long> IncrementAsync(CancellationToken ct = default);
}
