using System.Globalization;
using TicketHub.Application.Features.Events.GetEvents;

namespace TicketHub.Application.Common.Caching;

/// <summary>Cache key layout and lifetimes for SDD section 7.</summary>
public static class CacheKeys
{
    public const string CatalogGenerationKey = "events:catalog:generation";

    /// <summary>Bounds staleness if a generation increment is ever lost (e.g. Redis briefly unreachable).</summary>
    public static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(5);

    /// <summary>Short, so seat availability stays near real time while absorbing bursts of seat-map reads.</summary>
    public static readonly TimeSpan SeatMapTtl = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <c>events:catalog:gen:{generation}:page:{page}:search:{term}</c>, followed by every other filter so that
    /// distinct queries never share an entry. Search and city are case-insensitive, matching the query handler.
    /// </summary>
    public static string CatalogPage(long generation, GetEventsQuery query) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"events:catalog:gen:{generation}:page:{query.PageNumber}:search:{Normalize(query.Search)}" +
            $":size:{query.PageSize}:city:{Normalize(query.City)}:category:{query.Category}" +
            $":from:{query.StartDateFrom:O}:to:{query.StartDateTo:O}");

    public static string SeatMap(Guid eventId) => $"events:seatmap:{eventId:N}";

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
