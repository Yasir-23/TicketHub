using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Exceptions;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Entities;

namespace TicketHub.Application.Features.Auth.Login;

public sealed class LoginCommandHandler(
    IAppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        // Same error for unknown email and wrong password to avoid account enumeration.
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        var accessToken = jwtTokenService.CreateAccessToken(user);
        var refreshToken = jwtTokenService.CreateRefreshToken();

        user.SetRefreshToken(refreshToken.Token, refreshToken.ExpiresAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResponse(accessToken.Token, refreshToken.Token, accessToken.ExpiresInSeconds);
    }
}
