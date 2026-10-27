using MediatR;
using TicketHub.Domain.Enums;

namespace TicketHub.Application.Features.Reports.GetRevenueReport;

/// <summary>FR-REP-01: gross revenue from confirmed bookings, by category and event.</summary>
public sealed record GetRevenueReportQuery : IRequest<RevenueReportResponse>;

public sealed record RevenueReportResponse(
    long TotalRevenueCents,
    int TotalTicketsSold,
    int TotalBookings,
    IReadOnlyList<CategoryRevenueDto> Categories);

public sealed record CategoryRevenueDto(
    EventCategory Category,
    long RevenueCents,
    int TicketsSold,
    int BookingCount,
    IReadOnlyList<EventRevenueDto> Events);

public sealed record EventRevenueDto(
    Guid EventId,
    string Title,
    DateTime StartDateUtc,
    long RevenueCents,
    int TicketsSold,
    int BookingCount);
