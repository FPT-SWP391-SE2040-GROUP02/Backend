using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class AssetVersionConfiguration : IEntityTypeConfiguration<AssetVersion>
{
    public void Configure(EntityTypeBuilder<AssetVersion> builder)
    {
        builder.ToTable("AssetVersions");
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.AssetId, v.VersionNumber }).IsUnique();
        builder.Property(v => v.FileName).HasMaxLength(255);
        builder.Property(v => v.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(v => v.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(v => v.StorageKey).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.HasIndex(v => v.StorageKey).IsUnique();
        builder.Property(v => v.EncryptionKeyId).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(v => v.WrappedDataKey).HasMaxLength(60).IsFixedLength().IsRequired();
    }
}
