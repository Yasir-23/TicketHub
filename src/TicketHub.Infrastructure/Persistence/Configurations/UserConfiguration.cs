using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketHub.Domain.Entities;

namespace TicketHub.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.FullName)
            .HasMaxLength(UserConstraints.FullNameMaxLength)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasMaxLength(UserConstraints.EmailMaxLength)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(UserConstraints.PasswordHashMaxLength)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(u => u.RefreshToken)
            .HasMaxLength(UserConstraints.RefreshTokenMaxLength);

        builder.HasIndex(u => u.RefreshToken);
    }
}
