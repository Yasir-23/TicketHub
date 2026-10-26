using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
{
    public void Configure(EntityTypeBuilder<Seat> builder)
    {
        builder.ToTable("Seats");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.RowNumber)
            .HasMaxLength(SectionConstraints.RowNumberMaxLength)
            .IsRequired();

        // Npgsql maps a uint row version onto the xmin system column (SDD section 3).
        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.HasIndex(s => new { s.SectionId, s.RowNumber, s.SeatNumber })
            .IsUnique();
    }
}
