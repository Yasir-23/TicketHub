using MediatR;

namespace TicketHub.Application.Features.Venues.CreateVenue;

public sealed record CreateVenueCommand(string Name, string City, string Address) : IRequest<VenueResponse>;

public sealed record VenueResponse(Guid Id, string Name, string City, string Address, int TotalCapacity);
