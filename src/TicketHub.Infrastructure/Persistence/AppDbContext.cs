using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Common;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Exceptions;
using TicketHub.Infrastructure.Persistence.Configurations;

namespace TicketHub.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
    : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Section> Sections => Set<Section>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<EventSectionPricing> EventSectionPricings => Set<EventSectionPricing>();

    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySoftDeletes();
        ApplyAuditTimestamps();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception) when (IsActiveHoldCollision(exception))
        {
            throw new SeatAlreadyHeldException();
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplySoftDeletes();
        ApplyAuditTimestamps();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveHoldCollision(exception))
        {
            throw new SeatAlreadyHeldException();
        }
    }

    // BR-01: a rival transaction committed an active hold on the same seat first (SDD section 4.6).
    private static bool IsActiveHoldCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: SeatHoldConfiguration.ActiveHoldIndexName
        };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // BR-08: turn physical deletes of soft-deletable entities into IsDeleted updates.
    private void ApplySoftDeletes()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.MarkDeleted(utcNow);
            }
        }
    }

    private void ApplyAuditTimestamps()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedAtUtc).CurrentValue = utcNow;
                    break;
                case EntityState.Modified:
                    entry.Property(e => e.UpdatedAtUtc).CurrentValue = utcNow;
                    break;
            }
        }
    }
}
