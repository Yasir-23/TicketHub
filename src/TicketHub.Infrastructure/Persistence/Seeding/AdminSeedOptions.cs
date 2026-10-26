namespace TicketHub.Infrastructure.Persistence.Seeding;

/// <summary>
/// Optional bootstrap admin account. Seeding is skipped unless both Email and Password are configured,
/// so credentials live only in environment-specific configuration, never in source control.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "SeedAdmin";

    public string? Email { get; init; }

    public string? Password { get; init; }

    public string FullName { get; init; } = "TicketHub Administrator";
}
