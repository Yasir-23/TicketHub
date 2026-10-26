using TicketHub.Domain.Common;
using TicketHub.Domain.Enums;
using TicketHub.Domain.Exceptions;

namespace TicketHub.Domain.Entities;

public sealed class Booking : BaseEntity
{
    private readonly List<BookingItem> _bookingItems = [];
    private readonly List<PaymentTransaction> _paymentTransactions = [];
    private readonly List<SeatHold> _seatHolds = [];

    private Booking()
    {
    }

    public Guid EventId { get; private set; }

    public Event Event { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public User User { get; private set; } = null!;

    public long TotalAmountCents { get; private set; }

    public BookingStatus Status { get; private set; }

    public string TicketNumber { get; private set; } = string.Empty;

    public string QrCodePayload { get; private set; } = string.Empty;

    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public IReadOnlyCollection<BookingItem> BookingItems => _bookingItems;

    public IReadOnlyCollection<PaymentTransaction> PaymentTransactions => _paymentTransactions;

    public IReadOnlyCollection<SeatHold> SeatHolds => _seatHolds;

    public static Booking Create(
        Guid eventId,
        Guid customerId,
        long totalAmountCents,
        string ticketNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(totalAmountCents);

        var booking = new Booking
        {
            EventId = eventId,
            CustomerId = customerId,
            TotalAmountCents = totalAmountCents,
            TicketNumber = ticketNumber,
            QrCodePayload = string.Empty,
            Status = BookingStatus.PendingPayment
        };
        booking.QrCodePayload = $"ETICKET::{ticketNumber}::{booking.Id.ToString("N")[..8]}";
        return booking;
    }

    public void AddBookingItem(Guid seatId, long unitPriceCents) =>
        _bookingItems.Add(BookingItem.Create(Id, seatId, unitPriceCents));

    public void Confirm(DateTime confirmedAtUtc)
    {
        if (Status != BookingStatus.PendingPayment)
        {
            throw new DomainException($"Booking '{Id}' cannot be confirmed from {Status}.");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAtUtc = confirmedAtUtc;
    }

    public void Cancel(DateTime cancelledAtUtc)
    {
        if (Status == BookingStatus.Cancelled)
        {
            return;
        }

        if (Status != BookingStatus.Confirmed)
        {
            throw new DomainException($"Booking '{Id}' cannot be cancelled from {Status}.");
        }

        Status = BookingStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
    }
}
