using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TicketHub.Domain.Entities;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Infrastructure;

public sealed class SoftDeleteTests
{
    [Fact]
    public async Task Remove_OnSoftDeletableEntity_MarksDeletedAndHidesFromQueries()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var dbContext = TestDbContextFactory.Create(databaseName))
        {
            dbContext.Venues.Add(Venue.Create("Olympia Arena", "London", "Blythe Rd"));
            await dbContext.SaveChangesAsync();

            dbContext.Venues.Remove(await dbContext.Venues.SingleAsync());
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = TestDbContextFactory.Create(databaseName);
        (await verifyContext.Venues.CountAsync()).Should().Be(0);

        var stored = await verifyContext.Venues.IgnoreQueryFilters().SingleAsync();
        stored.IsDeleted.Should().BeTrue();
        stored.DeletedAtUtc.Should().Be(TestDbContextFactory.UtcNow.UtcDateTime);
    }
}
