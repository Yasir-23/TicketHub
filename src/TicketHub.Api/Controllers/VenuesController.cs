using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketHub.Api.Contracts;
using TicketHub.Application.Features.Venues.ConfigureSections;
using TicketHub.Application.Features.Venues.CreateVenue;
using TicketHub.Domain.Enums;

namespace TicketHub.Api.Controllers;

[ApiController]
[Route("api/v1/venues")]
[Authorize(Roles = nameof(UserRole.Admin))]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class VenuesController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<VenueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateVenue(CreateVenueCommand command, CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{venueId:guid}/sections")]
    [ProducesResponseType<ConfigureSectionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfigureSection(
        Guid venueId,
        ConfigureSectionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new ConfigureSectionsAndSeatsCommand(venueId, request.Name, request.Rows),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
