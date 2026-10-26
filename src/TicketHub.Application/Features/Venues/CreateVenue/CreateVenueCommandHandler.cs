using MediatR;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Venues.CreateVenue;

public sealed class CreateVenueCommandHandler(IAppDbContext dbContext)
    : IRequestHandler<CreateVenueCommand, VenueResponse>
{
    public async Task<VenueResponse> Handle(CreateVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = Venue.Create(request.Name, request.City, request.Address);

        dbContext.Venues.Add(venue);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new VenueResponse(venue.Id, venue.Name, venue.City, venue.Address, venue.TotalCapacity);
    }
}
