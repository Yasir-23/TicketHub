using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TicketHub.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>Set to false where migrations are applied out-of-band (e.g. a deploy step) or no database exists.</summary>
    public const string ApplyOnStartupKey = "Database:ApplyMigrationsOnStartup";

    /// <summary>
    /// Applies pending EF Core migrations before the app starts serving, so a fresh container brings its own schema.
    /// </summary>
    public static async Task ApplyDatabaseMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        if (!provider.GetRequiredService<IConfiguration>().GetValue(ApplyOnStartupKey, defaultValue: true))
        {
            return;
        }

        var dbContext = provider.GetRequiredService<AppDbContext>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator));

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation(
            "Applying {MigrationCount} pending migration(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied");
    }
}
