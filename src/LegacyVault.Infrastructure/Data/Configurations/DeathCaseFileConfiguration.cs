using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseFileConfiguration : IEntityTypeConfiguration<DeathCaseFile>
{
    public void Configure(EntityTypeBuilder<DeathCaseFile> builder)
    {
        builder.ToTable("DeathCaseFiles");

        builder.HasKey(f => f.Id);

        // Độ dài cột giống AssetVersionConfiguration.
        builder.Property(f => f.FileName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(f => f.StorageKey).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(f => f.EncryptionKeyId).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(f => f.WrappedDataKey).HasMaxLength(60).IsFixedLength().IsRequired();

        builder.HasIndex(f => f.VersionId);

        // D8: KHÔNG unique — phiên bản mới chép dòng file và dùng chung StorageKey; đếm để biết khi nào xóa nội dung.
        builder.HasIndex(f => f.StorageKey);
    }
}
