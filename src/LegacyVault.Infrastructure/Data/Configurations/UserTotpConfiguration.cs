using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class UserTotpConfiguration : IEntityTypeConfiguration<UserTotp>
{
    public void Configure(EntityTypeBuilder<UserTotp> builder)
    {
        builder.ToTable("UserTotps");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.KeyId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.WrappedSecret)
            .IsRequired()
            .HasMaxLength(256);

        // Mỗi tài khoản chỉ có một khóa TOTP.
        builder.HasIndex(t => t.UserId)
            .IsUnique();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
