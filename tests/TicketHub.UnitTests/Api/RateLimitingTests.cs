using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using TicketHub.Api.Controllers;
using TicketHub.Api.RateLimiting;

namespace TicketHub.UnitTests.Api;

/// <summary>NFR-04: policy registration, partitioning, and the 429 ProblemDetails contract.</summary>
public sealed class RateLimitingTests : IAsyncLifetime
{
    private const int Limit = 2;

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Window"] = "00:01:00",
            ["RateLimiting:AuthPermitLimit"] = Limit.ToString(),
            ["RateLimiting:BookingPermitLimit"] = Limit.ToString()
        });
        builder.Services.AddProblemDetails();
        builder.Services.AddTicketHubRateLimiting(builder.Configuration);

        _app = builder.Build();
        // Stand-ins for the client address and the authenticated user that the real pipeline provides.
        _app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Ip", out var ip))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
            }

            if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, user.ToString())],
                    "Test"));
            }

            return next(context);
        });
        _app.UseRateLimiter();
        _app.MapPost("/auth", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.Auth);
        _app.MapPost("/booking", () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.Booking);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task AuthPolicy_RejectsBeyondLimitWithProblemDetailsAndRetryAfter()
    {
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            (await Post("/auth", ip: "10.0.0.1")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rejected = await Post("/auth", ip: "10.0.0.1");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var retryAfter = int.Parse(rejected.Headers.GetValues("Retry-After").Single());
        retryAfter.Should().BeInRange(1, 60);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.Title.Should().Be("Too Many Requests");
        problem.Detail.Should().Contain($"{retryAfter} seconds");
        problem.Extensions.Should().ContainKey("retryAfterSeconds");
    }

    [Fact]
    public async Task AuthPolicy_PartitionsByClientIp()
    {
        for (var attempt = 0; attempt <= Limit; attempt++)
        {
            await Post("/auth", ip: "10.0.0.2");
        }

        (await Post("/auth", ip: "10.0.0.3")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BookingPolicy_PartitionsByUserRegardlessOfIp()
    {
        var alice = Guid.NewGuid().ToString();
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await Post("/booking", ip: $"10.1.0.{attempt}", user: alice);
        }

        (await Post("/booking", ip: "10.1.0.99", user: alice)).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "changing IP must not reset a user's budget");
        (await Post("/booking", ip: "10.1.0.99", user: Guid.NewGuid().ToString())).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BookingPolicy_FallsBackToIpForAnonymousCallers()
    {
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await Post("/booking", ip: "10.2.0.1");
        }

        (await Post("/booking", ip: "10.2.0.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Post("/booking", ip: "10.2.0.2")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Defaults_FollowSrsLimit()
    {
        var defaults = new RateLimitingOptions();

        defaults.Window.Should().Be(TimeSpan.FromMinutes(1));
        defaults.AuthPermitLimit.Should().Be(10);
        defaults.BookingPermitLimit.Should().Be(10);
    }

    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.Login), RateLimitPolicies.Auth)]
    [InlineData(typeof(AuthController), nameof(AuthController.Register), RateLimitPolicies.Auth)]
    [InlineData(typeof(EventsController), nameof(EventsController.HoldSeats), RateLimitPolicies.Booking)]
    [InlineData(typeof(BookingsController), nameof(BookingsController.Checkout), RateLimitPolicies.Booking)]
    public void Endpoints_ApplyExpectedPolicy(Type controller, string action, string policy)
    {
        EffectivePolicy(controller, action).Should().Be(policy);
    }

    [Theory]
    [InlineData(typeof(EventsController), nameof(EventsController.GetEvents))]
    [InlineData(typeof(EventsController), nameof(EventsController.GetSeatMap))]
    [InlineData(typeof(BookingsController), nameof(BookingsController.GetMyBookings))]
    public void ReadEndpoints_AreNotRateLimited(Type controller, string action)
    {
        EffectivePolicy(controller, action).Should().BeNull();
    }

    // Action-level attributes override controller-level ones, as in ASP.NET Core endpoint metadata.
    private static string? EffectivePolicy(Type controller, string action) =>
        (controller.GetMethod(action)!.GetCustomAttribute<EnableRateLimitingAttribute>()
            ?? controller.GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName;

    private Task<HttpResponseMessage> Post(string path, string ip, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Test-Ip", ip);
        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        return _client.SendAsync(request);
    }
}
