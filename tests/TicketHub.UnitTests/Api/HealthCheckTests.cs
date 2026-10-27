using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TicketHub.Api.Observability;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Api;

public sealed class HealthCheckTests(TicketHubApiFactory factory) : IClassFixture<TicketHubApiFactory>
{
    [Fact]
    public async Task Liveness_Returns200Healthy_EvenWhenDependenciesAreDown()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Readiness_Returns503WithPerDependencyStatus_WhenDependenciesAreDown()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
        var checks = body.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        checks.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            [HealthCheckExtensions.PostgresCheckName] = "Unhealthy",
            [HealthCheckExtensions.RedisCheckName] = "Unhealthy"
        });
        body.RootElement.ToString().Should().NotContain("127.0.0.1", "connection details must not leak");
    }

    [Fact]
    public async Task HealthEndpoints_AreAnonymousAndNotRateLimited()
    {
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 15; attempt++)
        {
            (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public void Readiness_RegistersPostgresAndRedisChecks_TaggedReady()
    {
        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Select(r => r.Name).Should().BeEquivalentTo(
            [HealthCheckExtensions.PostgresCheckName, HealthCheckExtensions.RedisCheckName]);
        registrations.Should().OnlyContain(r => r.Tags.Contains(HealthCheckExtensions.ReadyTag));
        registrations.Should().OnlyContain(r => r.Timeout == TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Readiness_WithoutRedisConfigured_ChecksOnlyPostgres()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TicketHubApiFactory.UnreachablePostgres
            })
            .Build();
        var services = new ServiceCollection().AddTicketHubHealthChecks(configuration).BuildServiceProvider();

        services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Select(r => r.Name).Should().Equal(HealthCheckExtensions.PostgresCheckName);
    }
}
