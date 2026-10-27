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
    /// <summary>Charges a seat hold and confirms the booking using an idempotency key.</summary>
    /// <param name="request">The hold identifier and payment details.</param>
    /// <param name="idempotencyKey">Required UUID used to safely replay checkout submissions.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">Payment succeeded and the booking was confirmed.</response>
    /// <response code="400">The idempotency key is missing or request validation failed.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not a customer.</response>
    /// <response code="402">The simulated payment provider declined the payment.</response>
    /// <response code="404">The hold was not found or does not belong to the customer.</response>
    /// <response code="409">The idempotency key is in use or conflicts with another checkout.</response>
    /// <response code="410">The seat hold expired before checkout completed.</response>
    /// <response code="422">Checkout violates a business rule or the event has started.</response>
    /// <response code="429">The client exceeded the booking rate limit.</response>
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

    /// <summary>Cancels a customer's booking when the event is at least 48 hours away.</summary>
    /// <param name="id">Identifier of the booking to cancel.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The booking was cancelled and its seats were released.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not a customer.</response>
    /// <response code="404">The booking was not found or does not belong to the customer.</response>
    /// <response code="422">The booking is within the 48-hour cancellation cutoff.</response>
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

    /// <summary>Lists the authenticated customer's active and historical bookings.</summary>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <response code="200">The customer's booking history.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The authenticated user is not a customer.</response>
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
