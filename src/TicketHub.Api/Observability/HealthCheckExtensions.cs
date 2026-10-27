using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using InfrastructureRegistration = TicketHub.Infrastructure.DependencyInjection;

namespace TicketHub.Api.Observability;

public static class HealthCheckExtensions
{
    public const string LivenessPath = "/health";
    public const string ReadinessPath = "/health/ready";
    public const string ReadyTag = "ready";
    public const string PostgresCheckName = "postgres";
    public const string RedisCheckName = "redis";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Readiness dependencies: PostgreSQL always; Redis when a Redis connection string is configured (without one
    /// the app runs on its in-process cache and does not depend on Redis). The Redis check reuses the application's
    /// multiplexer instead of opening a second connection.
    /// </summary>
    public static IServiceCollection AddTicketHubHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks();

        var postgres = configuration.GetConnectionString(InfrastructureRegistration.ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            healthChecks.AddNpgSql(postgres, name: PostgresCheckName, tags: [ReadyTag], timeout: CheckTimeout);
        }

        var redis = configuration.GetConnectionString(InfrastructureRegistration.RedisConnectionStringName);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            healthChecks.AddRedis(
                provider => provider.GetRequiredService<IConnectionMultiplexer>(),
                name: RedisCheckName,
                tags: [ReadyTag],
                timeout: CheckTimeout);
        }

        return services;
    }

    /// <summary>
    /// <c>/health</c> is liveness: it runs no dependency checks, so a database outage never gets a healthy process
    /// restarted. <c>/health/ready</c> is readiness: 200 only when every dependency is healthy, otherwise 503.
    /// </summary>
    public static IEndpointRouteBuilder MapTicketHubHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(ReadyTag),
                ResponseWriter = WriteReadinessAsync
            })
            .AllowAnonymous();

        return endpoints;
    }

    // Reports each dependency's status and timing; exception details stay in the logs, not the response.
    private static Task WriteReadinessAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1)
            })
        }));
    }
}
