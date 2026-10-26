using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("Venues");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Name)
            .HasMaxLength(VenueConstraints.NameMaxLength)
            .IsRequired();

        builder.Property(v => v.City)
            .HasMaxLength(VenueConstraints.CityMaxLength)
            .IsRequired();

        builder.Property(v => v.Address)
            .HasMaxLength(VenueConstraints.AddressMaxLength)
            .IsRequired();

        builder.HasIndex(v => v.City);

        builder.HasMany(v => v.Sections)
            .WithOne(s => s.Venue)
            .HasForeignKey(s => s.VenueId)
            .OnDelete(DeleteBehavior.Cascade);

        // BR-08: a venue referenced by events must never be physically deleted.
        builder.HasMany(v => v.Events)
            .WithOne(e => e.Venue)
            .HasForeignKey(e => e.VenueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(v => !v.IsDeleted);
    }
}
