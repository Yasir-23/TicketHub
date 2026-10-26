using TicketHub.Domain.Common;
using TicketHub.Domain.Enums;

namespace TicketHub.Domain.Entities;

public sealed class User : BaseEntity
{
    // Required by EF Core.
    private User()
    {
    }

    public string FullName { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public string? RefreshToken { get; private set; }

    public DateTime? RefreshTokenExpiresAtUtc { get; private set; }

    public static User CreateCustomer(string fullName, string email, string passwordHash) =>
        Create(fullName, email, passwordHash, UserRole.Customer);

    public static User CreateAdmin(string fullName, string email, string passwordHash) =>
        Create(fullName, email, passwordHash, UserRole.Admin);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetRefreshToken(string refreshToken, DateTime expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        RefreshToken = refreshToken;
        RefreshTokenExpiresAtUtc = expiresAtUtc;
    }

    public void RevokeRefreshToken()
    {
        RefreshToken = null;
        RefreshTokenExpiresAtUtc = null;
    }

    private static User Create(string fullName, string email, string passwordHash, UserRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User
        {
            FullName = fullName.Trim(),
            Email = NormalizeEmail(email),
            PasswordHash = passwordHash,
            Role = role
        };
    }
}
