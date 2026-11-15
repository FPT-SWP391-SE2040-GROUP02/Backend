using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ExecutorKeyShardConfiguration : IEntityTypeConfiguration<ExecutorKeyShard>
{
    public void Configure(EntityTypeBuilder<ExecutorKeyShard> builder)
    {
        builder.ToTable("ExecutorKeyShards");

        // Mỗi kho có tối đa một mảnh hiện hành: khóa chính chính là VaultId.
        builder.HasKey(s => s.VaultId);

        // SHA-256 hex = 64 ký tự.
        builder.Property(s => s.ShardHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(s => s.EncryptedShard)
            .HasMaxLength(512)
            .IsUnicode(false);

        builder.Property(s => s.EncryptionKeyId)
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(s => s.WrappedDataKey)
            .HasMaxLength(60)
            .IsFixedLength();

        // Hai request cấp lại song song không được ghi đè lẫn nhau.
        builder.Property(s => s.RowVersion)
            .IsRowVersion();

        builder.HasOne<Vault>()
            .WithOne()
            .HasForeignKey<ExecutorKeyShard>(s => s.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
