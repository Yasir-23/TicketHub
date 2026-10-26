using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.ToTable("BookingItems");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.UnitPriceCents).IsRequired();

        builder.HasIndex(i => i.SeatId);
        builder.HasIndex(i => new { i.BookingId, i.SeatId }).IsUnique();

        builder.HasOne(i => i.Seat)
            .WithMany()
            .HasForeignKey(i => i.SeatId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
