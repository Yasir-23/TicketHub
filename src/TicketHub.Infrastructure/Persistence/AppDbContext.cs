using Microsoft.EntityFrameworkCore;
using TicketHub.Application.Common.Interfaces;
using TicketHub.Domain.Common;
using TicketHub.Domain.Entities;

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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySoftDeletes();
        ApplyAuditTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplySoftDeletes();
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

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
