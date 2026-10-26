using TicketHub.Domain.Common;

namespace TicketHub.Domain.Entities;

public sealed class BookingItem : BaseEntity
{
    private BookingItem()
    {
    }

    public Guid BookingId { get; private set; }

    public Booking Booking { get; private set; } = null!;

    public Guid SeatId { get; private set; }

    public Seat Seat { get; private set; } = null!;

    public long UnitPriceCents { get; private set; }

    internal static BookingItem Create(Guid bookingId, Guid seatId, long unitPriceCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unitPriceCents);

        return new BookingItem
        {
            BookingId = bookingId,
            SeatId = seatId,
            UnitPriceCents = unitPriceCents
        };
    }
}
