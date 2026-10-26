namespace TicketHub.Domain.Exceptions;

/// <summary>
/// A business rule was violated by an otherwise well-formed request.
/// </summary>
public class DomainException(string message) : Exception(message);
