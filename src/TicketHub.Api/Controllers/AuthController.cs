using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketHub.Api.RateLimiting;
using TicketHub.Application.Features.Auth.Login;
using TicketHub.Application.Features.Auth.RegisterCustomer;

namespace TicketHub.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
[Produces("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>Registers a customer account using the submitted name, email, and password.</summary>
    /// <param name="command">Customer registration details.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="201">The customer account was created.</response>
    /// <response code="400">The registration details failed validation.</response>
    /// <response code="409">An account with the email already exists.</response>
    /// <response code="429">The client exceeded the authentication rate limit.</response>
    [HttpPost("register")]
    [ProducesResponseType<RegisterCustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(RegisterCustomerCommand command, CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Authenticates a customer or administrator and issues access and refresh tokens.</summary>
    /// <param name="command">Login email and password.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">Authentication succeeded and tokens are returned.</response>
    /// <response code="400">The login request failed validation.</response>
    /// <response code="401">The email or password is invalid.</response>
    /// <response code="429">The client exceeded the authentication rate limit.</response>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return Ok(response);
    }
}
