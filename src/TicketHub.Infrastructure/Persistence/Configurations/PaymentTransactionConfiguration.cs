using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(t => t.AmountCents).IsRequired();
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.ProviderReference).HasMaxLength(128).IsRequired();

        builder.HasIndex(t => t.IdempotencyKey).IsUnique();
        builder.HasIndex(t => new { t.BookingId, t.CreatedAtUtc });
    }
}
