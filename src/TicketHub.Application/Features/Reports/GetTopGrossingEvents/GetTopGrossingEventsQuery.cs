using MediatR;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Reports.GetTopGrossingEvents;

/// <summary>FR-REP-03: the highest-grossing events of all time, from confirmed bookings.</summary>
public sealed record GetTopGrossingEventsQuery : IRequest<IReadOnlyList<TopGrossingEventDto>>
{
    public const int Limit = 5;
}

public sealed record TopGrossingEventDto(
    int Rank,
    Guid EventId,
    string Title,
    EventCategory Category,
    DateTime StartDateUtc,
    long RevenueCents,
    int TicketsSold,
    int BookingCount);
