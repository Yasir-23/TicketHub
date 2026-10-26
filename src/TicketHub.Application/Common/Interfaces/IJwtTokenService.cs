using TicketHub.Domain.Entities;

namespace TicketHub.Application.Common.Interfaces;

public interface IJwtTokenService
{
    GeneratedAccessToken CreateAccessToken(User user);

    GeneratedRefreshToken CreateRefreshToken();
}

public sealed record GeneratedAccessToken(string Token, int ExpiresInSeconds);

public sealed record GeneratedRefreshToken(string Token, DateTime ExpiresAtUtc);
