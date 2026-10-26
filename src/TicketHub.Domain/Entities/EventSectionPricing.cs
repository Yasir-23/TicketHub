using TicketHub.Domain.Common;

namespace TicketHub.Domain.Entities;

public sealed class EventSectionPricing : BaseEntity
{
    // Required by EF Core.
    private EventSectionPricing()
    {
    }

    public Guid EventId { get; private set; }

    public Event Event { get; private set; } = null!;

    public Guid SectionId { get; private set; }

    public Section Section { get; private set; } = null!;

    public long PriceCents { get; private set; }

    internal static EventSectionPricing Create(Guid eventId, Guid sectionId, long priceCents) => new()
    {
        EventId = eventId,
        SectionId = sectionId,
        PriceCents = priceCents
    };

    internal void UpdatePrice(long priceCents) => PriceCents = priceCents;
}
