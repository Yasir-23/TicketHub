using TicketHub.Application.Features.Venues.ConfigureSections;

namespace TicketHub.Api.Contracts;

/// <summary>
/// Body of POST /api/v1/venues/{venueId}/sections; the venue id comes from the route.
/// </summary>
public sealed record ConfigureSectionRequest(string Name, IReadOnlyList<SectionRowDto> Rows);
