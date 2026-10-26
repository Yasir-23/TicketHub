namespace TicketHub.Domain.Exceptions;

public sealed class PaymentFailedException()
    : DomainException("The payment could not be completed.");
