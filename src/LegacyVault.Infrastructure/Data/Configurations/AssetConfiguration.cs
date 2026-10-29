using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.UpdatedAt).IsConcurrencyToken();
        builder.HasIndex(a => new { a.VaultId, a.CreatedAt });
        builder.Property(a => a.FileName).HasMaxLength(255);
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.HasIndex(a => a.StorageKey).IsUnique();
        builder.Property(a => a.EncryptionKeyId).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(a => a.WrappedDataKey).HasMaxLength(60).IsFixedLength().IsRequired();

        builder.HasOne(a => a.Vault).WithMany()
            .HasForeignKey(a => a.VaultId).OnDelete(DeleteBehavior.Restrict);
    }
}
