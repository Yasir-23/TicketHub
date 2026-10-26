using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Status).HasConversion<int>().IsRequired();
        builder.Property(b => b.TicketNumber).HasMaxLength(32).IsRequired();
        builder.Property(b => b.QrCodePayload).HasMaxLength(256).IsRequired();
        builder.Property(b => b.TotalAmountCents).IsRequired();

        builder.HasIndex(b => b.TicketNumber).IsUnique();
        builder.HasIndex(b => new { b.CustomerId, b.CreatedAtUtc });

        builder.HasOne(b => b.Event)
            .WithMany()
            .HasForeignKey(b => b.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.BookingItems)
            .WithOne(i => i.Booking)
            .HasForeignKey(i => i.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.PaymentTransactions)
            .WithOne(t => t.Booking)
            .HasForeignKey(t => t.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
