using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> builder)
    {
        builder.ToTable("Sections");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name)
            .HasMaxLength(SectionConstraints.NameMaxLength)
            .IsRequired();

        // Section names are unique per venue among live sections; soft-deleted names can be reused.
        builder.HasIndex(s => new { s.VenueId, s.Name })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasMany(s => s.Seats)
            .WithOne(seat => seat.Section)
            .HasForeignKey(seat => seat.SectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
