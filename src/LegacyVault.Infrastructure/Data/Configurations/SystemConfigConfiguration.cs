using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.ToTable("SystemConfigs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ServiceKey)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.Slot)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.SettingsJson)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(c => c.EncryptionKeyId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.WrappedDataKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.SecretsEncrypted)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(c => c.UpdateReason)
            .IsRequired()
            .HasMaxLength(500);

        // Hai Admin lưu cùng lúc: người sau nhận EditConflict thay vì ghi đè.
        builder.Property(c => c.Version)
            .IsConcurrencyToken();

        // Mỗi dịch vụ chỉ có một ô chính và một ô dự phòng.
        builder.HasIndex(c => new { c.ServiceKey, c.Slot })
            .IsUnique();
    }
}
