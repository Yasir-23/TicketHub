using MediatR;
using TicketHub.Application.Common.Models;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Events.GetEvents;

public sealed record GetEventsQuery : IRequest<PagedResult<EventSummaryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public string? City { get; init; }

    public EventCategory? Category { get; init; }

    public DateTime? StartDateFrom { get; init; }

    public DateTime? StartDateTo { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;
}

public sealed record EventSummaryDto(
    Guid Id,
    string Title,
    EventCategory Category,
    string VenueName,
    string City,
    DateTime StartDateUtc,
    long? MinPriceCents);
