using Serilog;
using Serilog.Events;

namespace TicketHub.Api.Observability;

public static class LoggingExtensions
{
    public const string ConsoleOutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Structured console logging. Levels and overrides come from the "Serilog" configuration section, so they can
    /// be tuned per environment (e.g. <c>Serilog__MinimumLevel__Default=Debug</c>) without a rebuild.
    /// </summary>
    public static LoggerConfiguration ConfigureTicketHubLogging(
        this LoggerConfiguration logger,
        IConfiguration configuration,
        IServiceProvider services) =>
        logger
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: ConsoleOutputTemplate);

    /// <summary>
    /// One summary line per request. Health probes (every few seconds from Docker or an orchestrator) are logged at
    /// Verbose so they do not drown out real traffic, unless they fail.
    /// </summary>
    public static IApplicationBuilder UseTicketHubRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : context.Request.Path.StartsWithSegments(HealthCheckExtensions.LivenessPath)
                        ? LogEventLevel.Verbose
                        : LogEventLevel.Information;
        });
}
