using TicketHub.Domain.Common;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Domain.Entities;

public sealed class Event : SoftDeletableEntity
{
    private readonly List<EventSectionPricing> _sectionPricings = [];

    // Required by EF Core.
    private Event()
    {
    }

    public Guid VenueId { get; private set; }

    public Venue Venue { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public EventCategory Category { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DateTime StartDateUtc { get; private set; }

    public DateTime EndDateUtc { get; private set; }

    public IReadOnlyCollection<EventSectionPricing> SectionPricings => _sectionPricings;

    public static Event Create(
        Guid venueId,
        string title,
        EventCategory category,
        string? description,
        DateTime startDateUtc,
        DateTime endDateUtc,
        DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var start = startDateUtc.AsUtc();
        var end = endDateUtc.AsUtc();

        if (start <= utcNow)
        {
            throw new DomainException("Event start date must be in the future.");
        }

        if (end <= start)
        {
            throw new DomainException("Event end date must be after its start date.");
        }

        return new Event
        {
            VenueId = venueId,
            Title = title.Trim(),
            Category = category,
            Description = description?.Trim() ?? string.Empty,
            StartDateUtc = start,
            EndDateUtc = end
        };
    }

    /// <summary>
    /// BR-03: an event is read-only once <c>StartDateUtc &lt;= UtcNow</c>.
    /// </summary>
    public bool HasStarted(DateTime utcNow) => StartDateUtc <= utcNow;

    public void EnsureModifiable(DateTime utcNow)
    {
        if (HasStarted(utcNow))
        {
            throw new EventAlreadyStartedException(Id);
        }
    }

    public void SetSectionPrice(Guid sectionId, long priceCents, DateTime utcNow)
    {
        EnsureModifiable(utcNow);

        if (priceCents < 0)
        {
            throw new DomainException("Section price cannot be negative.");
        }

        var existing = _sectionPricings.FirstOrDefault(p => p.SectionId == sectionId);
        if (existing is not null)
        {
            existing.UpdatePrice(priceCents);
            return;
        }

        _sectionPricings.Add(EventSectionPricing.Create(Id, sectionId, priceCents));
    }
}
