using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketHub.Application.Features.Reports.GetDailySalesReport;
using TicketHub.Application.Features.Reports.GetEventOccupancyReport;
using TicketHub.Application.Features.Reports.GetRevenueReport;
using TicketHub.Application.Features.Reports.GetTopGrossingEvents;
using TicketHub.Domain.Enums;

namespace TicketHub.Api.Controllers;

/// <summary>Executive reporting (docs/API.md section 6, FR-REP-01 to FR-REP-04).</summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize(Roles = nameof(UserRole.Admin))]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ReportsController(ISender sender) : ControllerBase
{
    [HttpGet("revenue")]
    [ProducesResponseType<RevenueReportResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevenue(CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetRevenueReportQuery(), cancellationToken));
    }

    [HttpGet("occupancy/{eventId:guid}")]
    [ProducesResponseType<EventOccupancyReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOccupancy(Guid eventId, CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetEventOccupancyReportQuery(eventId), cancellationToken));
    }

    [HttpGet("top-events")]
    [ProducesResponseType<IReadOnlyList<TopGrossingEventDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopEvents(CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetTopGrossingEventsQuery(), cancellationToken));
    }

    [HttpGet("daily-sales")]
    [ProducesResponseType<DailySalesReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDailySales(
        [FromQuery] GetDailySalesReportQuery query,
        CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(query, cancellationToken));
    }
}
