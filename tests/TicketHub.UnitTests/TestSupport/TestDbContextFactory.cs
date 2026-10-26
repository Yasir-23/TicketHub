using Microsoft.EntityFrameworkCore;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.UnitTests.TestSupport;

internal static class TestDbContextFactory
{
    public static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    public static AppDbContext Create(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, new FixedTimeProvider(UtcNow));
    }
}
