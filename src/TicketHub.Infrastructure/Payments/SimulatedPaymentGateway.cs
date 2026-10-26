using System.Net;
using System.Net.Http.Json;
using Polly.Timeout;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Enums;

namespace TicketHub.Infrastructure.Payments;

public sealed class SimulatedPaymentGateway(HttpClient httpClient) : IPaymentGateway
{
    public async Task<PaymentGatewayResult> ChargeAsync(
        string paymentMethod,
        string paymentToken,
        long amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "charge",
                new SimulatedPaymentRequest(paymentMethod, paymentToken, amountCents),
                cancellationToken);

            var providerReference = response.Headers.TryGetValues("X-Provider-Reference", out var references)
                ? references.First()
                : string.Empty;

            return response.StatusCode switch
            {
                HttpStatusCode.OK => new PaymentGatewayResult(PaymentStatus.Success, providerReference),
                HttpStatusCode.PaymentRequired => new PaymentGatewayResult(PaymentStatus.Failed, providerReference),
                HttpStatusCode.GatewayTimeout => new PaymentGatewayResult(PaymentStatus.TimedOut, providerReference),
                _ => throw new HttpRequestException(
                    $"Unexpected simulated payment response: {(int)response.StatusCode}.")
            };
        }
        catch (TimeoutRejectedException)
        {
            return new PaymentGatewayResult(PaymentStatus.TimedOut, "timeout");
        }
    }
}

internal sealed class SimulatedPaymentHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var payment = await request.Content!.ReadFromJsonAsync<SimulatedPaymentRequest>(
            cancellationToken: cancellationToken);
        if (payment is null)
        {
            throw new HttpRequestException("The simulated payment request was empty.");
        }

        var result = payment.PaymentToken switch
        {
            "tok_visa_valid" => HttpStatusCode.OK,
            "tok_fail" => HttpStatusCode.PaymentRequired,
            "tok_timeout" => HttpStatusCode.GatewayTimeout,
            _ => Random.Shared.Next(100) switch
            {
                < 80 => HttpStatusCode.OK,
                < 90 => HttpStatusCode.PaymentRequired,
                _ => HttpStatusCode.GatewayTimeout
            }
        };

        var response = new HttpResponseMessage(result);
        response.Headers.TryAddWithoutValidation("X-Provider-Reference", $"sim_{Guid.NewGuid():N}");
        return response;
    }
}

internal sealed record SimulatedPaymentRequest(string PaymentMethod, string PaymentToken, long AmountCents);
