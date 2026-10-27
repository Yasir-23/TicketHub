using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketHub.Api.Contracts;
using TicketHub.Api.Extensions;
using TicketHub.Api.RateLimiting;
using TicketHub.Application.Features.Bookings.CancelBooking;
using TicketHub.Application.Features.Bookings.CheckoutBooking;
using TicketHub.Application.Features.Bookings.GetMyBookings;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Api.Controllers;

[ApiController]
[Route("api/v1/bookings")]
[Authorize(Roles = nameof(UserRole.Customer))]
[Produces("application/json")]
public sealed class BookingsController(ISender sender) : ControllerBase
{
    [HttpPost("checkout")]
    [EnableRateLimiting(RateLimitPolicies.Booking)]
    [ProducesResponseType<CheckoutBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutBookingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new MissingIdempotencyKeyException();
        }

        var response = await sender.Send(
            new CheckoutBookingCommand(
                request.HoldId,
                customerId,
                request.PaymentMethod,
                request.PaymentToken,
                idempotencyKey),
            cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<CancelBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        return Ok(await sender.Send(new CancelBookingCommand(id, customerId), cancellationToken));
    }

    [HttpGet("my-bookings")]
    [ProducesResponseType<IReadOnlyList<MyBookingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyBookings(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        return Ok(await sender.Send(new GetMyBookingsQuery(customerId), cancellationToken));
    }
}
