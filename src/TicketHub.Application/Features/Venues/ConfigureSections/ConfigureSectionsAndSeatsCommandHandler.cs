using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Venues.ConfigureSections;

public sealed class ConfigureSectionsAndSeatsCommandHandler(IAppDbContext dbContext)
    : IRequestHandler<ConfigureSectionsAndSeatsCommand, ConfigureSectionResponse>
{
    public async Task<ConfigureSectionResponse> Handle(
        ConfigureSectionsAndSeatsCommand request,
        CancellationToken cancellationToken)
    {
        var venue = await dbContext.Venues.SingleOrDefaultAsync(v => v.Id == request.VenueId, cancellationToken)
            ?? throw new NotFoundException(nameof(Venue), request.VenueId);

        var normalizedName = request.Name.Trim().ToLower();
        var nameTaken = await dbContext.Sections.AnyAsync(
            s => s.VenueId == venue.Id && s.Name.ToLower() == normalizedName,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"Venue already has a section named '{request.Name.Trim()}'.");
        }

        var section = venue.AddSection(
            request.Name,
            request.Rows.Select(r => new SeatRowSpecification(r.RowNumber, r.SeatCount)));

        dbContext.Sections.Add(section);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConfigureSectionResponse(section.Id, venue.Id, section.Name, section.Capacity);
    }
}
