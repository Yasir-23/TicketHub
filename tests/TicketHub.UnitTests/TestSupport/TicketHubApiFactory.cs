using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.Infrastructure.Persistence;

namespace TicketHub.UnitTests.TestSupport;

/// <summary>
/// Runs the real API pipeline (JWT bearer, role authorization, controllers, ProblemDetails, health checks) with an
/// in-memory database and without background workers or startup migrations. The PostgreSQL and Redis connection
/// strings point at closed local ports, so nothing ever reaches a developer's real database or cache.
/// </summary>
public sealed class TicketHubApiFactory : WebApplicationFactory<Program>
{
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=2";
    public const string UnreachableRedis = "127.0.0.1:1,connectTimeout=1000";

    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (host settings) is visible while Program registers services, unlike late configuration sources.
        builder.UseSetting("ConnectionStrings:DefaultConnection", UnreachablePostgres);
        builder.UseSetting("ConnectionStrings:Redis", UnreachableRedis);
        builder.UseSetting(DatabaseMigrator.ApplyOnStartupKey, "false");
        builder.UseSetting("Jwt:SigningKey", "tickethub-api-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Jwt:Issuer", "TicketHub");
        builder.UseSetting("Jwt:Audience", "TicketHub.Api");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => TestDbContextFactory.Configure(options, _databaseName));

            var workers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.Namespace == typeof(ExpiredHoldReaperWorker).Namespace)
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }
        });
    }

    public HttpClient CreateClientFor(UserRole? role)
    {
        var client = CreateClient();
        if (role is { } userRole)
        {
            var user = userRole == UserRole.Admin
                ? User.CreateAdmin("Ada Admin", "admin@example.com", "hash")
                : User.CreateCustomer("Cal Customer", "customer@example.com", "hash");
            var token = Services.GetRequiredService<IJwtTokenService>().CreateAccessToken(user).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}
