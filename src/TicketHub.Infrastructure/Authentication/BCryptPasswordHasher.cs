using TicketHub.Application.Common.Interfaces;

namespace TicketHub.Infrastructure.Authentication;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public const int DefaultWorkFactor = 12;

    private readonly int _workFactor;

    public BCryptPasswordHasher()
        : this(DefaultWorkFactor)
    {
    }

    public BCryptPasswordHasher(int workFactor)
    {
        _workFactor = workFactor;
    }

    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed stored hash must never authenticate.
            return false;
        }
    }
}
