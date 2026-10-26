namespace TicketHub.Domain.Common;

public static class DateTimeExtensions
{
    /// <summary>
    /// Normalizes a value to <see cref="DateTimeKind.Utc"/>. Unspecified values are assumed to already be UTC,
    /// since every date in the API contract is named and documented as UTC.
    /// </summary>
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
