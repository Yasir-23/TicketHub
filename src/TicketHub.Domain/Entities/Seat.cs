using TicketHub.Domain.Common;

namespace TicketHub.Domain.Entities;

/// <summary>
/// A physical seat in a venue section. Seats are shared by every event at the venue, so availability is
/// not stored here: it is derived per event from <see cref="SeatHold"/> records.
/// </summary>
public sealed class Seat : BaseEntity
{
    // Required by EF Core.
    private Seat()
    {
    }

    public Guid SectionId { get; private set; }

    public Section Section { get; private set; } = null!;

    public string RowNumber { get; private set; } = string.Empty;

    public int SeatNumber { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, mapped to PostgreSQL's xmin system column.
    /// </summary>
    public uint RowVersion { get; private set; }

    internal static Seat Create(Guid sectionId, string rowNumber, int seatNumber) => new()
    {
        SectionId = sectionId,
        RowNumber = rowNumber,
        SeatNumber = seatNumber
    };
}
