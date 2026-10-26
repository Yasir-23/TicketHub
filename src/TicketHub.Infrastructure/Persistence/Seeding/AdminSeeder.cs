using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Seeding;

public static class AdminSeeder
{
    public static async Task SeedAdminUserAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var options = provider.GetRequiredService<IOptions<AdminSeedOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            return;
        }

        var dbContext = provider.GetRequiredService<AppDbContext>();
        var email = User.NormalizeEmail(options.Email);

        if (await dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return;
        }

        var passwordHasher = provider.GetRequiredService<IPasswordHasher>();
        dbContext.Users.Add(User.CreateAdmin(options.FullName, email, passwordHasher.Hash(options.Password)));
        await dbContext.SaveChangesAsync(cancellationToken);

        provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(AdminSeeder))
            .LogInformation("Seeded admin user {Email}", email);
    }
}
