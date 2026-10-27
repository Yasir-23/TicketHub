namespace TicketHub.Application.Common.Models;

/// <summary>
/// A confirmed booking whose ticket still has to be rendered and delivered (FR-JOB-02, SDD section 6.2).
/// </summary>
public sealed record TicketFulfillmentMessage(
    Guid BookingId,
    Guid CustomerId,
    string Email,
    string TicketNumber,
    string QrPayload,
    string EventTitle,
    DateTime StartDateUtc);
