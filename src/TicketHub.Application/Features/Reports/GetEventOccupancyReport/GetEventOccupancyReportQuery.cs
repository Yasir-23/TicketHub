using MediatR;

namespace TicketHub.Application.Features.Reports.GetEventOccupancyReport;

/// <summary>FR-REP-02: real-time Sold / Held / Available distribution for an event, overall and per section.</summary>
public sealed record GetEventOccupancyReportQuery(Guid EventId) : IRequest<EventOccupancyReportResponse>;

/// <param name="OccupancyPercentage">Sold seats as a percentage of total seats, rounded to 2 decimal places.</param>
public sealed record EventOccupancyReportResponse(
    Guid EventId,
    string Title,
    int TotalSeats,
    int SoldSeats,
    int HeldSeats,
    int AvailableSeats,
    decimal OccupancyPercentage,
    IReadOnlyList<SectionOccupancyDto> Sections);

public sealed record SectionOccupancyDto(
    Guid SectionId,
    string SectionName,
    int TotalSeats,
    int SoldSeats,
    int HeldSeats,
    int AvailableSeats,
    decimal OccupancyPercentage);
