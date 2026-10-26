namespace TicketHub.Application.Common.Exceptions;

public sealed class NotFoundException(string resourceName, object key)
    : Exception($"{resourceName} '{key}' was not found.");
