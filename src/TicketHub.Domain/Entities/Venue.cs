using TicketHub.Domain.Common;

namespace TicketHub.Domain.Entities;

public sealed class Venue : SoftDeletableEntity
{
    private readonly List<Section> _sections = [];
    private readonly List<Event> _events = [];

    // Required by EF Core.
    private Venue()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public int TotalCapacity { get; private set; }

    public IReadOnlyCollection<Section> Sections => _sections;

    public IReadOnlyCollection<Event> Events => _events;

    public static Venue Create(string name, string city, string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        return new Venue
        {
            Name = name.Trim(),
            City = city.Trim(),
            Address = address.Trim()
        };
    }

    /// <summary>
    /// Adds a section and generates its seats from the row layout (FR-VEN-02, FR-VEN-03).
    /// </summary>
    public Section AddSection(string name, IEnumerable<SeatRowSpecification> rows)
    {
        var section = Section.Create(Id, name, rows);

        _sections.Add(section);
        TotalCapacity += section.Capacity;

        return section;
    }
}
