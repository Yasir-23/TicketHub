using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Application.Features.Auth.Login;
using TicketHub.Domain.Entities;
using TicketHub.Infrastructure.Authentication;
using TicketHub.Infrastructure.Persistence;
using TicketHub.UnitTests.TestSupport;

namespace TicketHub.UnitTests.Application.Auth;

public sealed class LoginCommandHandlerTests
{
    private const string Email = "jane.doe@example.com";
    private const string Password = "Password123!";

    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IJwtTokenService> _jwtTokenService = new();
    private readonly DateTime _refreshExpiresAtUtc = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    public LoginCommandHandlerTests()
    {
        _passwordHasher.Setup(h => h.Verify(Password, "stored-hash")).Returns(true);
        _jwtTokenService.Setup(s => s.CreateAccessToken(It.IsAny<User>()))
            .Returns(new GeneratedAccessToken("access-token", 3600));
        _jwtTokenService.Setup(s => s.CreateRefreshToken())
            .Returns(new GeneratedRefreshToken("refresh-token", _refreshExpiresAtUtc));
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsTokensAndStoresRefreshToken()
    {
        await using var dbContext = await CreateDbWithUserAsync("stored-hash");
        var handler = CreateHandler(dbContext);

        var response = await handler.Handle(new LoginCommand("Jane.Doe@Example.com", Password), CancellationToken.None);

        response.Should().Be(new LoginResponse("access-token", "refresh-token", 3600));

        var user = dbContext.Users.Single();
        user.RefreshToken.Should().Be("refresh-token");
        user.RefreshTokenExpiresAtUtc.Should().Be(_refreshExpiresAtUtc);
        user.UpdatedAtUtc.Should().Be(TestDbContextFactory.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ThrowsInvalidCredentialsAndIssuesNoTokens()
    {
        await using var dbContext = await CreateDbWithUserAsync("stored-hash");
        var handler = CreateHandler(dbContext);

        var act = () => handler.Handle(new LoginCommand(Email, "WrongPassword1!"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        _jwtTokenService.Verify(s => s.CreateAccessToken(It.IsAny<User>()), Times.Never);
        dbContext.Users.Single().RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithUnknownEmail_ThrowsInvalidCredentials()
    {
        await using var dbContext = TestDbContextFactory.Create();
        var handler = CreateHandler(dbContext);

        var act = () => handler.Handle(new LoginCommand("nobody@example.com", Password), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        _passwordHasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithRealHasherAndTokenService_IssuesVerifiableJwt()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        await using var dbContext = await CreateDbWithUserAsync(hasher.Hash(Password));
        var jwtOptions = new JwtOptions
        {
            Issuer = "TicketHub",
            Audience = "TicketHub.Api",
            SigningKey = "unit-test-signing-key-with-at-least-32-chars"
        };
        var tokenService = new JwtTokenService(Options.Create(jwtOptions), TimeProvider.System);
        var handler = new LoginCommandHandler(dbContext, hasher, tokenService);

        var response = await handler.Handle(new LoginCommand(Email, Password), CancellationToken.None);

        response.ExpiresInSeconds.Should().Be(3600);
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            response.AccessToken,
            new TokenValidationParameters
            {
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = jwtOptions.CreateSigningKey()
            });

        validation.IsValid.Should().BeTrue();
        validation.Claims[JwtRegisteredClaimNames.Email].Should().Be(Email);
        validation.Claims[JwtTokenService.RoleClaimType].Should().Be("Customer");
    }

    private LoginCommandHandler CreateHandler(AppDbContext dbContext) =>
        new(dbContext, _passwordHasher.Object, _jwtTokenService.Object);

    private static async Task<AppDbContext> CreateDbWithUserAsync(string passwordHash)
    {
        var dbContext = TestDbContextFactory.Create();
        dbContext.Users.Add(User.CreateCustomer("Jane Doe", Email, passwordHash));
        await dbContext.SaveChangesAsync();
        return dbContext;
    }
}
