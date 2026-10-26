using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Events.CreateEvent;

public sealed class CreateEventCommandHandler(IAppDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<CreateEventCommand, CreateEventResponse>
{
    public async Task<CreateEventResponse> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Venues.AnyAsync(v => v.Id == request.VenueId, cancellationToken))
        {
            throw new NotFoundException(nameof(Venue), request.VenueId);
        }

        var requestedSectionIds = request.SectionPricing.Select(p => p.SectionId).ToList();
        var venueSectionIds = await dbContext.Sections
            .Where(s => s.VenueId == request.VenueId && requestedSectionIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var foreignSectionIds = requestedSectionIds.Except(venueSectionIds).ToList();
        if (foreignSectionIds.Count != 0)
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    nameof(CreateEventCommand.SectionPricing),
                    $"Sections do not belong to venue '{request.VenueId}': {string.Join(", ", foreignSectionIds)}.")
            ]);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var @event = Event.Create(
            request.VenueId,
            request.Title,
            request.Category,
            request.Description,
            request.StartDateUtc,
            request.EndDateUtc,
            utcNow);

        foreach (var price in request.SectionPricing)
        {
            @event.SetSectionPrice(price.SectionId, price.PriceCents, utcNow);
        }

        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateEventResponse(@event.Id, @event.Title, @event.Category, @event.StartDateUtc);
    }
}
