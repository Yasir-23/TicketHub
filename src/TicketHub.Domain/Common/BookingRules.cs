namespace TicketHub.Domain.Common;

public static class BookingRules
{
    /// <summary>BR-02: maximum seats a customer may hold or own for one event.</summary>
    public const int MaxTicketsPerCustomerPerEvent = 6;

    /// <summary>BR-04: a seat hold expires exactly 10 minutes after creation.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);
}
