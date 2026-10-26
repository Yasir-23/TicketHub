using MediatR;

namespace TicketHub.Application.Features.Holds.HoldSeats;

/// <param name="CustomerId">Taken from the caller's JWT, never from the request body.</param>
public sealed record HoldSeatsCommand(Guid EventId, Guid CustomerId, IReadOnlyList<Guid> SeatIds)
    : IRequest<HoldSeatsResponse>;

public sealed record HoldSeatsResponse(
    Guid HoldId,
    Guid EventId,
    IReadOnlyList<HeldSeatDto> HeldSeats,
    long TotalPriceCents,
    DateTime HoldExpiresAtUtc);

public sealed record HeldSeatDto(Guid SeatId, string Row, int SeatNumber, long PriceCents);
