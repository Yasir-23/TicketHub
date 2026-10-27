using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using TicketHub.Application.Features.Reports.GetEventOccupancyReport;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Api;

public sealed class ReportsAuthorizationTests(TicketHubApiFactory factory) : IClassFixture<TicketHubApiFactory>
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
