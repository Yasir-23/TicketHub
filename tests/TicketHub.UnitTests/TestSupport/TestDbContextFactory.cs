using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.UnitTests.TestSupport;

internal static class TestDbContextFactory
{
    public static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    public static AppDbContext Create(string? databaseName = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        Configure(builder, databaseName ?? Guid.NewGuid().ToString());

        return new AppDbContext(builder.Options, new FixedTimeProvider(UtcNow));
    }

    /// <summary>Shared by <see cref="Create"/> and DI-registered contexts (e.g. for background worker tests).</summary>
    public static void Configure(DbContextOptionsBuilder builder, string databaseName) =>
        builder
            .UseInMemoryDatabase(databaseName)
            // The in-memory provider has no transactions; handlers that open one still run correctly.
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
}
