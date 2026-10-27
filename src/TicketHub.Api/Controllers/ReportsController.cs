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
    /// <summary>Returns gross revenue totals grouped by event and category.</summary>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The revenue report.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
    [HttpGet("revenue")]
    [ProducesResponseType<RevenueReportResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevenue(CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetRevenueReportQuery(), cancellationToken));
    }

    /// <summary>Returns sold, held, and available seat counts for each section of an event.</summary>
    /// <param name="eventId">Identifier of the event to report.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The event occupancy report.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
    /// <response code="404">The event was not found.</response>
    [HttpGet("occupancy/{eventId:guid}")]
    [ProducesResponseType<EventOccupancyReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOccupancy(Guid eventId, CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetEventOccupancyReportQuery(eventId), cancellationToken));
    }

    /// <summary>Returns the five highest-grossing events.</summary>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The top grossing event report.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
    [HttpGet("top-events")]
    [ProducesResponseType<IReadOnlyList<TopGrossingEventDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopEvents(CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetTopGrossingEventsQuery(), cancellationToken));
    }

    /// <summary>Returns daily revenue, booking, and ticket totals for an inclusive date range.</summary>
    /// <param name="query">Start and end dates for the report.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The daily sales report.</response>
    /// <response code="400">The date range failed validation.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
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
