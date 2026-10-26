using TicketHub.Domain.Common;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Domain.Entities;

public sealed class Section : SoftDeletableEntity
{
    private readonly List<Seat> _seats = [];

    // Required by EF Core.
    private Section()
    {
    }

    public Guid VenueId { get; private set; }

    public Venue Venue { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    public int Capacity { get; private set; }

    public IReadOnlyCollection<Seat> Seats => _seats;

    internal static Section Create(Guid venueId, string name, IEnumerable<SeatRowSpecification> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rows);

        var section = new Section
        {
            VenueId = venueId,
            Name = name.Trim()
        };

        var seenRows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var rowNumber = row.RowNumber?.Trim().ToUpperInvariant() ?? string.Empty;

            if (rowNumber.Length == 0)
            {
                throw new DomainException("Row number is required.");
            }

            if (row.SeatCount <= 0)
            {
                throw new DomainException($"Row '{rowNumber}' must contain at least one seat.");
            }

            if (!seenRows.Add(rowNumber))
            {
                throw new DomainException($"Row '{rowNumber}' is defined more than once.");
            }

            for (var seatNumber = 1; seatNumber <= row.SeatCount; seatNumber++)
            {
                section._seats.Add(Seat.Create(section.Id, rowNumber, seatNumber));
            }
        }

        if (section._seats.Count == 0)
        {
            throw new DomainException("A section must contain at least one row.");
        }

        section.Capacity = section._seats.Count;
        return section;
    }
}
