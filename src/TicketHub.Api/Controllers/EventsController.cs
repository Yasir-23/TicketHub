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

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<EventSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEvents([FromQuery] GetEventsQuery query, CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(query, cancellationToken));
    }

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

    [HttpGet("{id:guid}/seat-map")]
    [AllowAnonymous]
    [ProducesResponseType<EventSeatMapResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeatMap(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await sender.Send(new GetEventSeatMapQuery(id), cancellationToken));
    }
}
