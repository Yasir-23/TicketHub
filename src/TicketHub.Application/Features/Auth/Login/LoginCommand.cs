using MediatR;
using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Application.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResponse>, ISensitiveRequest;

public sealed record LoginResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds);
