using TicketHub.Domain.Common;
using TicketHub.Domain.Enums;

namespace TicketHub.Domain.Entities;

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

    public SeatStatus Status { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, mapped to PostgreSQL's xmin system column.
    /// </summary>
    public uint RowVersion { get; private set; }

    internal static Seat Create(Guid sectionId, string rowNumber, int seatNumber) => new()
    {
        SectionId = sectionId,
        RowNumber = rowNumber,
        SeatNumber = seatNumber,
        Status = SeatStatus.Available
    };
}
