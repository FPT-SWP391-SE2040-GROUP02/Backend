using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class EkycConsentConfiguration : IEntityTypeConfiguration<EkycConsent>
{
    public void Configure(EntityTypeBuilder<EkycConsent> builder)
    {
        builder.ToTable("EkycConsents");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Purpose)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.NoticeVersion)
            .IsRequired()
            .HasMaxLength(32)
            .IsUnicode(false);

        // Tìm lần đồng ý mới nhất của một người dùng theo mục đích.
        builder.HasIndex(c => new { c.UserId, c.Purpose, c.ConsentedAt });

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
