using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;
using TicketHub.Domain.Enums;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class SeatHoldConfiguration : IEntityTypeConfiguration<SeatHold>
{
    /// <summary>
    /// BR-01 safety net (SDD section 4.6): at most one active hold per seat per event.
    /// </summary>
    public const string ActiveHoldIndexName = "UX_SeatHolds_Active";

    public void Configure(EntityTypeBuilder<SeatHold> builder)
    {
        builder.ToTable("SeatHolds");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(h => new { h.EventId, h.SeatId })
            .HasDatabaseName(ActiveHoldIndexName)
            .HasFilter($"\"Status\" = {(int)HoldStatus.Active}")
            .IsUnique();

        // Supports the BR-02 per-customer count and checkout lookups.
        builder.HasIndex(h => new { h.EventId, h.CustomerId, h.Status });
        builder.HasIndex(h => h.HoldGroupId);
        builder.HasIndex(h => new { h.Status, h.HoldExpiresAtUtc });

        // Holds are history: never cascade-delete them from their event, seat or customer.
        builder.HasOne(h => h.Event)
            .WithMany()
            .HasForeignKey(h => h.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Seat)
            .WithMany()
            .HasForeignKey(h => h.SeatId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Customer)
            .WithMany()
            .HasForeignKey(h => h.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Booking)
            .WithMany(b => b.SeatHolds)
            .HasForeignKey(h => h.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
