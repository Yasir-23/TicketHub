using MediatR;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Events.CreateEvent;

public sealed record CreateEventCommand(
    Guid VenueId,
    string Title,
    EventCategory Category,
    string? Description,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    IReadOnlyList<SectionPriceDto> SectionPricing) : IRequest<CreateEventResponse>;

public sealed record SectionPriceDto(Guid SectionId, long PriceCents);

public sealed record CreateEventResponse(Guid Id, string Title, EventCategory Category, DateTime StartDateUtc);
