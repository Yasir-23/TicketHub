using FluentAssertions;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.UnitTests.Infrastructure;

public sealed class CustomerEventLockKeyTests
{
    [Fact]
    public void CreateLockKey_IsDeterministicAndDependsOnBothIds()
    {
        var customerId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        var key = PostgresAdvisoryCustomerEventLock.CreateLockKey(customerId, eventId);

        PostgresAdvisoryCustomerEventLock.CreateLockKey(customerId, eventId).Should().Be(key);
        PostgresAdvisoryCustomerEventLock.CreateLockKey(Guid.NewGuid(), eventId).Should().NotBe(key);
        PostgresAdvisoryCustomerEventLock.CreateLockKey(customerId, Guid.NewGuid()).Should().NotBe(key);
    }

    [Fact]
    public void CreateLockKey_IsNotSymmetric()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        PostgresAdvisoryCustomerEventLock.CreateLockKey(a, b)
            .Should().NotBe(PostgresAdvisoryCustomerEventLock.CreateLockKey(b, a));
    }
}
