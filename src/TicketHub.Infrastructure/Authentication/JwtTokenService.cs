using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Authentication;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : IJwtTokenService
{
    public const string RoleClaimType = "role";

    private const int RefreshTokenByteLength = 64;

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly JwtOptions _options = options.Value;

    public GeneratedAccessToken CreateAccessToken(User user)
    {
        var issuedAt = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt.Add(lifetime),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName),
                new Claim(RoleClaimType, user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            SigningCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256)
        };

        return new GeneratedAccessToken(TokenHandler.CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }

    public GeneratedRefreshToken CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenByteLength));
        var expiresAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenLifetimeDays);

        return new GeneratedRefreshToken(token, expiresAtUtc);
    }
}
