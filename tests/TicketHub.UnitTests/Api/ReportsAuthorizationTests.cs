using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Features.Reports.GetEventOccupancyReport;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.BackgroundJobs;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Api;

/// <summary>
/// Runs the real API pipeline (JWT bearer, role authorization, controllers, ProblemDetails) with an in-memory
/// database and without background workers.
/// </summary>
public sealed class ReportsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "reports-authorization-tests-signing-key-0123456789",
                ["Jwt:Issuer"] = "TicketHub",
                ["Jwt:Audience"] = "TicketHub.Api"
            }));

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

public sealed class ReportsAuthorizationTests(ReportsApiFactory factory) : IClassFixture<ReportsApiFactory>
{
    public static TheoryData<string> Endpoints => new()
    {
        "/api/v1/reports/revenue",
        $"/api/v1/reports/occupancy/{Guid.NewGuid()}",
        "/api/v1/reports/top-events",
        "/api/v1/reports/daily-sales?from=2026-10-01&to=2026-10-31"
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Anonymous_IsRejectedWith401(string url)
    {
        using var client = factory.CreateClientFor(role: null);

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Customer_IsRejectedWith403(string url)
    {
        using var client = factory.CreateClientFor(UserRole.Customer);

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/v1/reports/revenue")]
    [InlineData("/api/v1/reports/top-events")]
    [InlineData("/api/v1/reports/daily-sales?from=2026-10-01&to=2026-10-31")]
    public async Task Admin_GetsReport(string url)
    {
        using var client = factory.CreateClientFor(UserRole.Admin);

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_Occupancy_ReturnsReportForExistingEvent()
    {
        var eventId = await SeedEventAsync();
        using var client = factory.CreateClientFor(UserRole.Admin);

        var response = await client.GetAsync($"/api/v1/reports/occupancy/{eventId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<EventOccupancyReportResponse>();
        report!.TotalSeats.Should().Be(3);
        report.AvailableSeats.Should().Be(3);
    }

    [Fact]
    public async Task Admin_Occupancy_ForUnknownEvent_Returns404()
    {
        using var client = factory.CreateClientFor(UserRole.Admin);

        var response = await client.GetAsync($"/api/v1/reports/occupancy/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Admin_DailySales_WithInvertedRange_Returns400ValidationProblem()
    {
        using var client = factory.CreateClientFor(UserRole.Admin);

        var response = await client.GetAsync("/api/v1/reports/daily-sales?from=2026-10-31&to=2026-10-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("to");
    }

    [Theory]
    [InlineData("/api/v1/reports/daily-sales")]
    [InlineData("/api/v1/reports/daily-sales?from=not-a-date&to=2026-10-01")]
    public async Task Admin_DailySales_WithMissingOrMalformedDates_Returns400(string url)
    {
        using var client = factory.CreateClientFor(UserRole.Admin);

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> SeedEventAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var venue = Venue.Create("Olympia Arena", "London", "Blythe Rd");
        var section = venue.AddSection("VIP", [new SeatRowSpecification("A", 3)]);
        var @event = Event.Create(venue.Id, "Coldplay", EventCategory.Concert, null, now.AddDays(10), now.AddDays(10).AddHours(3), now);
        @event.SetSectionPrice(section.Id, 15000, now);
        dbContext.Venues.Add(venue);
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync();
        return @event.Id;
    }
}
