namespace TicketHub.Domain.Entities;

/// <summary>
/// One row of a section layout: seats 1..<see cref="SeatCount"/> are generated for <see cref="RowNumber"/>.
/// </summary>
public sealed record SeatRowSpecification(string RowNumber, int SeatCount);
