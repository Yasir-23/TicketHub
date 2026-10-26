namespace TicketHub.Domain.Entities;

public static class UserConstraints
{
    public const int FullNameMaxLength = 200;
    public const int EmailMaxLength = 256;
    public const int PasswordHashMaxLength = 100;
    public const int RefreshTokenMaxLength = 200;
}
