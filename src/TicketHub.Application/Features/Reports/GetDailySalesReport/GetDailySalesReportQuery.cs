using MediatR;

namespace TicketHub.Application.Features.Reports.GetDailySalesReport;

/// <summary>
/// FR-REP-04: confirmed sales per UTC calendar day, by confirmation date, for the inclusive range
/// [<see cref="From"/>, <see cref="To"/>].
/// </summary>
public sealed record GetDailySalesReportQuery : IRequest<DailySalesReportResponse>
{
    public const int MaxRangeDays = 366;

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <param name="Days">Every day in the range, in order; days without sales have zero counts.</param>
public sealed record DailySalesReportResponse(
    DateOnly From,
    DateOnly To,
    long TotalRevenueCents,
    int TotalBookings,
    int TotalTicketsSold,
    IReadOnlyList<DailySalesDto> Days);

public sealed record DailySalesDto(DateOnly Date, int BookingCount, int TicketsSold, long RevenueCents);
