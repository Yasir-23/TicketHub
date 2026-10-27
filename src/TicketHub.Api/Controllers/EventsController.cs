using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketHub.Api.Contracts;
using TicketHub.Api.Extensions;
using TicketHub.Api.RateLimiting;
using TicketHub.Application.Common.Models;
using TicketHub.Application.Features.Holds.HoldSeats;
using TicketHub.Application.Features.Events.CreateEvent;
using TicketHub.Application.Features.Events.GetEvents;
using TicketHub.Application.Features.Events.GetEventSeatMap;
using TicketHub.Domain.Enums;

namespace TicketHub.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Produces("application/json")]
public sealed class EventsController(ISender sender) : ControllerBase
{
    /// <summary>Creates an event and configures its ticket prices by venue section.</summary>
    /// <param name="command">Event information, schedule, venue, and section pricing.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="201">The event was created.</response>
    /// <response code="400">The event details or section pricing failed validation.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
    /// <response code="404">The venue or a requested resource was not found.</response>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [ProducesResponseType<CreateEventResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateEvent(CreateEventCommand command, CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Searches and filters the published event catalog with pagination.</summary>
    /// <param name="query">Search term, city, category, date range, and pagination options.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The requested page of matching events.</response>
    /// <response code="400">A query parameter failed validation.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<EventSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEvents([FromQuery] GetEventsQuery query, CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(query, cancellationToken));
    }

    /// <summary>Places a temporary hold on one or more seats for an event.</summary>
    /// <param name="id">Identifier of the event.</param>
    /// <param name="request">Seat identifiers to hold; a customer may hold no more than six seats per event.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="201">The seats were held for ten minutes.</response>
    /// <response code="400">The requested seats failed validation.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not a customer.</response>
    /// <response code="404">The event or one or more seats were not found or are not on sale.</response>
    /// <response code="409">One or more seats are already held or booked.</response>
    /// <response code="422">The hold would exceed the per-event ticket limit or the event has started.</response>
    /// <response code="429">The client exceeded the booking rate limit.</response>
    [HttpPost("{id:guid}/holds")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    [EnableRateLimiting(RateLimitPolicies.Booking)]
    [ProducesResponseType<HoldSeatsResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> HoldSeats(Guid id, HoldSeatsRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        var response = await sender.Send(new HoldSeatsCommand(id, customerId, request.SeatIds), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Returns the event's priced sections and per-event seat availability.</summary>
    /// <param name="id">Identifier of the event.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The event seat map and current availability.</response>
    /// <response code="404">The event was not found.</response>
    [HttpGet("{id:guid}/seat-map")]
    [AllowAnonymous]
    [ProducesResponseType<EventSeatMapResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeatMap(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetEventSeatMapQuery(id), cancellationToken));
    }
}
