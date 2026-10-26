using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        if (request is ISensitiveRequest)
        {
            logger.LogInformation("Handling {RequestName} (payload redacted)", requestName);
        }
        else
        {
            logger.LogInformation("Handling {RequestName} {@Request}", requestName, request);
        }

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var response = await next();

            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds} ms",
                requestName,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "{RequestName} failed after {ElapsedMilliseconds} ms",
                requestName,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }
}
