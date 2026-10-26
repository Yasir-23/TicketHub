using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using TicketHub.Domain.Entities;
using TicketHub.Infrastructure.Authentication;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Infrastructure;

public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly JwtTokenService _service = new(
        Options.Create(new JwtOptions
        {
            Issuer = "TicketHub",
            Audience = "TicketHub.Api",
            SigningKey = "unit-test-signing-key-with-at-least-32-chars",
            AccessTokenLifetimeMinutes = 60,
            RefreshTokenLifetimeDays = 7
        }),
        new FixedTimeProvider(Now));

    [Fact]
    public void CreateAccessToken_ContainsUserClaimsAndConfiguredLifetime()
    {
        var user = User.CreateCustomer("Jane Doe", "jane.doe@example.com", "hash");

        var result = _service.CreateAccessToken(user);

        result.ExpiresInSeconds.Should().Be(3600);

        var token = new JsonWebToken(result.Token);
        token.Alg.Should().Be("HS256");
        token.Issuer.Should().Be("TicketHub");
        token.Audiences.Should().ContainSingle().Which.Should().Be("TicketHub.Api");
        token.Subject.Should().Be(user.Id.ToString());
        token.GetClaim(JwtRegisteredClaimNames.Email).Value.Should().Be("jane.doe@example.com");
        token.GetClaim(JwtTokenService.RoleClaimType).Value.Should().Be("Customer");
        token.ValidTo.Should().Be(Now.UtcDateTime.AddHours(1));
    }

    [Fact]
    public void CreateRefreshToken_ReturnsUniqueOpaqueTokenWithConfiguredExpiry()
    {
        var first = _service.CreateRefreshToken();
        var second = _service.CreateRefreshToken();

        first.Token.Should().NotBe(second.Token);
        first.Token.Length.Should().BeGreaterThanOrEqualTo(64);
        first.Token.Length.Should().BeLessThanOrEqualTo(UserConstraints.RefreshTokenMaxLength);
        first.ExpiresAtUtc.Should().Be(Now.UtcDateTime.AddDays(7));
    }
}
