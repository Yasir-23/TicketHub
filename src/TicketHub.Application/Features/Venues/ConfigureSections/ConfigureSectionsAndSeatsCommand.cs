using MediatR;

namespace TicketHub.Application.Features.Venues.ConfigureSections;

public sealed record ConfigureSectionsAndSeatsCommand(Guid VenueId, string Name, IReadOnlyList<SectionRowDto> Rows)
    : IRequest<ConfigureSectionResponse>;

public sealed record SectionRowDto(string RowNumber, int SeatCount);

public sealed record ConfigureSectionResponse(Guid SectionId, Guid VenueId, string Name, int TotalSeatsGenerated);
