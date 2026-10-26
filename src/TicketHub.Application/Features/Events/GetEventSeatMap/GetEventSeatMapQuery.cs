using MediatR;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Events.GetEventSeatMap;

public sealed record GetEventSeatMapQuery(Guid EventId) : IRequest<EventSeatMapResponse>;

public sealed record EventSeatMapResponse(Guid EventId, IReadOnlyList<SeatMapSectionDto> Sections);

public sealed record SeatMapSectionDto(
    Guid SectionId,
    string SectionName,
    long PriceCents,
    IReadOnlyList<SeatMapSeatDto> Seats);

public sealed record SeatMapSeatDto(Guid SeatId, string Row, int Number, SeatStatus Status);
