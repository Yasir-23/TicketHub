namespace TicketHub.Api.Contracts;

/// <summary>
/// Body of POST /api/v1/events/{id}/holds; the event id comes from the route and the customer from the JWT.
/// </summary>
public sealed record HoldSeatsRequest(IReadOnlyList<Guid> SeatIds);
