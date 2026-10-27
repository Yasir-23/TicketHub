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
    /// <summary>Creates a venue with the submitted name, city, and address.</summary>
    /// <param name="command">Venue details.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="201">The venue was created.</response>
    /// <response code="400">The venue details failed validation.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
    [HttpPost]
    [ProducesResponseType<VenueResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateVenue(CreateVenueCommand command, CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Creates a venue section and generates its rows and seats from the submitted layout.</summary>
    /// <param name="venueId">Identifier of the venue receiving the section.</param>
    /// <param name="request">Section name and row/seat counts.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="201">The section and its seats were created.</response>
    /// <response code="400">The section layout failed validation.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not an administrator.</response>
    /// <response code="404">The venue was not found.</response>
    /// <response code="409">The venue already contains a section with this name.</response>
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
