using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Common.Models;

namespace TicketHub.Infrastructure.BackgroundJobs;

/// <summary>
/// Consumes <see cref="ITicketFulfillmentChannel"/> (FR-JOB-02, SDD section 6.2): renders the ticket pass,
/// verifies its QR payload and simulates email delivery.
/// </summary>
/// <remarks>
/// Graceful shutdown: <see cref="StopAsync"/> completes the channel instead of abandoning the read loop, so every
/// message accepted before shutdown is still fulfilled. Only the host's shutdown deadline cuts the drain short.
/// </remarks>
public sealed class TicketFulfillmentBackgroundWorker(
    ITicketFulfillmentChannel channel,
    IOptions<TicketFulfillmentOptions> options,
    TimeProvider timeProvider,
    ILogger<TicketFulfillmentBackgroundWorker> logger) : BackgroundService
{
    private const string QrPrefix = "ETICKET";

    private readonly CancellationTokenSource _shutdownDeadline = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ticket fulfillment worker started");

        // Deliberately not bound to stoppingToken: the loop ends when StopAsync completes the channel and the
        // backlog is drained, or when the host's shutdown deadline passes.
        try
        {
            await foreach (var message in channel.ReadAllAsync(_shutdownDeadline.Token))
            {
                try
                {
                    await FulfillAsync(message, _shutdownDeadline.Token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(
                        exception,
                        "Ticket fulfillment failed for booking {BookingId} ({TicketNumber})",
                        message.BookingId,
                        message.TicketNumber);
                }
            }

            logger.LogInformation("Ticket fulfillment worker stopped after draining the channel");
        }
        catch (OperationCanceledException) when (_shutdownDeadline.IsCancellationRequested)
        {
            logger.LogWarning("Ticket fulfillment worker hit the shutdown deadline before draining the channel");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        channel.Complete();
        await using var registration = cancellationToken.Register(_shutdownDeadline.Cancel);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _shutdownDeadline.Dispose();
        base.Dispose();
    }

    /// <summary>Returns true when the ticket was delivered; false when its QR payload failed verification.</summary>
    internal async Task<bool> FulfillAsync(TicketFulfillmentMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var startedAt = Stopwatch.GetTimestamp();

        if (!TryVerifyQrPayload(message, out var qrFingerprint))
        {
            logger.LogError(
                "Ticket {TicketNumber} for booking {BookingId} failed QR verification; delivery skipped",
                message.TicketNumber,
                message.BookingId);
            return false;
        }

        logger.LogInformation(
            "Ticket {TicketNumber} QR verified (fingerprint {QrFingerprint}) in {ElapsedMilliseconds} ms",
            message.TicketNumber,
            qrFingerprint,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        await Task.Delay(settings.SimulatedRenderDelay, timeProvider, cancellationToken);
        logger.LogInformation(
            "Ticket {TicketNumber} pass rendered for {EventTitle} at {StartDateUtc:O} in {ElapsedMilliseconds} ms",
            message.TicketNumber,
            message.EventTitle,
            message.StartDateUtc,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        await Task.Delay(settings.SimulatedEmailDelay, timeProvider, cancellationToken);
        logger.LogInformation(
            "Ticket {TicketNumber} for booking {BookingId} emailed to {MaskedEmail}; fulfilled in {ElapsedMilliseconds} ms",
            message.TicketNumber,
            message.BookingId,
            MaskEmail(message.Email),
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        return true;
    }

    /// <summary>
    /// The payload issued at checkout is <c>ETICKET::{TicketNumber}::{first 8 hex chars of BookingId}</c>.
    /// A payload that does not bind to this booking and ticket is never delivered.
    /// </summary>
    internal static bool TryVerifyQrPayload(TicketFulfillmentMessage message, out string fingerprint)
    {
        fingerprint = string.Empty;

        var parts = message.QrPayload.Split("::");
        if (parts.Length != 3
            || parts[0] != QrPrefix
            || parts[1] != message.TicketNumber
            || parts[2] != message.BookingId.ToString("N")[..8])
        {
            return false;
        }

        fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.QrPayload)))[..16];
        return true;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "(no email)" : $"{email[0]}***{email[at..]}";
    }
}
