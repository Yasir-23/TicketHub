using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TicketHub.Api.Extensions;

namespace TicketHub.Api.RateLimiting;

public static class RateLimitPolicies
{
    /// <summary>Login and registration, partitioned by client IP.</summary>
    public const string Auth = "auth-policy";

    /// <summary>Seat holds and checkout, partitioned by authenticated user (client IP as a fallback).</summary>
    public const string Booking = "booking-policy";
}

/// <summary>NFR-04: fixed-window limits per partition. Defaults follow the SRS (10 attempts per minute).</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    [Range(1, 10_000)]
    public int AuthPermitLimit { get; set; } = 10;

    [Range(1, 10_000)]
    public int BookingPermitLimit { get; set; } = 10;
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddTicketHubRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
            {
                var limits = settings.Value;

                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.OnRejected = WriteRejectionAsync;

                limiter.AddPolicy(RateLimitPolicies.Auth, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        $"ip:{ClientIp(context)}",
                        _ => FixedWindow(limits.AuthPermitLimit, limits.Window)));

                limiter.AddPolicy(RateLimitPolicies.Booking, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.User.TryGetUserId(out var userId) ? $"user:{userId:N}" : $"ip:{ClientIp(context)}",
                        _ => FixedWindow(limits.BookingPermitLimit, limits.Window)));
            });

        return services;
    }

    /// <summary>RFC 7807 body plus a Retry-After header (whole seconds, rounded up).</summary>
    internal static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "Rate limit exceeded. Please retry later."
        };

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            problemDetails.Detail = $"Rate limit exceeded. Retry after {seconds} seconds.";
            problemDetails.Extensions["retryAfterSeconds"] = seconds;
        }

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        if (!await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            }))
        {
            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        }
    }

    private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit, TimeSpan window) =>
        new()
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        };

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
