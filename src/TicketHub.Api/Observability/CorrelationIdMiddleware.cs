using System.Diagnostics;
using Serilog.Context;

namespace TicketHub.Api.Observability;

/// <summary>
/// Gives every request a correlation ID: the caller's <c>X-Correlation-ID</c> when it is well-formed, otherwise
/// the W3C trace ID (the same value carried in ProblemDetails <c>traceId</c>). It is echoed in the response and
/// attached to every log event written while the request runs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogPropertyName = "CorrelationId";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = IsWellFormed(context.Request.Headers[HeaderName].ToString())
            ? context.Request.Headers[HeaderName].ToString()
            : Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    // Rejects oversized or unusual values so callers cannot inject arbitrary text into logs and headers.
    internal static bool IsWellFormed(string value) =>
        value.Length is > 0 and <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
