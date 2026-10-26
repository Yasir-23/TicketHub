using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    private const int CategoryMaxLength = 32;

    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Title)
            .HasMaxLength(EventConstraints.TitleMaxLength)
            .IsRequired();

        builder.Property(e => e.Category)
            .HasConversion<string>()
            .HasMaxLength(CategoryMaxLength)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(EventConstraints.DescriptionMaxLength)
            .IsRequired();

        builder.HasIndex(e => e.StartDateUtc);
        builder.HasIndex(e => e.Category);

        builder.HasMany(e => e.SectionPricings)
            .WithOne(p => p.Event)
            .HasForeignKey(p => p.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
