using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class EventSectionPricingConfiguration : IEntityTypeConfiguration<EventSectionPricing>
{
    public void Configure(EntityTypeBuilder<EventSectionPricing> builder)
    {
        builder.ToTable("EventSectionPricings");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.PriceCents)
            .HasColumnType("bigint")
            .IsRequired();

        builder.HasOne(p => p.Section)
            .WithMany()
            .HasForeignKey(p => p.SectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.EventId, p.SectionId })
            .IsUnique();
    }
}
