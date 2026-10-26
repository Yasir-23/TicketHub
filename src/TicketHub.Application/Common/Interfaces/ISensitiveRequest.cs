namespace TicketHub.Application.Common.Interfaces;

/// <summary>
/// Marks a MediatR request whose payload contains secrets (e.g. passwords) and must not be logged.
/// </summary>
public interface ISensitiveRequest;
