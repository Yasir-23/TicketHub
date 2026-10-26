using TicketHub.Domain.Enums;

namespace TicketHub.Application.Common.Interfaces;

public interface IPaymentGateway
{
    Task<PaymentGatewayResult> ChargeAsync(
        string paymentMethod,
        string paymentToken,
        long amountCents,
        CancellationToken cancellationToken);
}

public sealed record PaymentGatewayResult(PaymentStatus Status, string ProviderReference);
