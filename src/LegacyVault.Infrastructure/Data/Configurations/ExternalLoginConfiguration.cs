using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.ToTable("ExternalLogins");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(e => e.ProviderSubject)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.ProviderEmail)
            .HasMaxLength(256);

        // Một tài khoản Google chỉ gắn với một tài khoản LegacyVault.
        builder.HasIndex(e => new { e.Provider, e.ProviderSubject })
            .IsUnique();

        // Một tài khoản LegacyVault gắn tối đa một tài khoản của mỗi nhà cung cấp.
        builder.HasIndex(e => new { e.UserId, e.Provider })
            .IsUnique();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
