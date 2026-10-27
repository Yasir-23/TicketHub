namespace TicketHub.UnitTests.TestSupport;

/// <summary>A clock tests can move forward mid-operation (e.g. while a payment is "in flight").</summary>
internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
