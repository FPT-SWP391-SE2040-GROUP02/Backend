using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class BeneficiaryConfiguration : IEntityTypeConfiguration<Beneficiary>
{
    public void Configure(EntityTypeBuilder<Beneficiary> builder)
    {
        builder.ToTable("Beneficiaries");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.CitizenIdLast4)
            .IsRequired()
            .HasMaxLength(4)
            .IsUnicode(false)
            .IsFixedLength();

        builder.Property(b => b.EncryptionKeyId)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        // Giống AssetVersion: nonce 12 + DEK 32 + tag 16 = 60 byte (DataKeyProtector.WrappedKeySize).
        builder.Property(b => b.WrappedDataKey)
            .IsRequired()
            .HasMaxLength(60)
            .IsFixedLength();

        builder.Property(b => b.IdentityEncrypted)
            .IsRequired();

        builder.Property(b => b.ContactEncrypted)
            .IsRequired();

        // ETag của hồ sơ (UpdateBeneficiaryRequest.Etag), giống Container.
        builder.Property(b => b.UpdatedAt)
            .IsConcurrencyToken();

        // KHÔNG unique: hai CCCD khác nhau có thể trùng 4 số cuối. Dùng để dò trùng CCCD (QUOTA-04) và tìm kiếm.
        builder.HasIndex(b => new { b.VaultId, b.CitizenIdLast4 });
        builder.HasIndex(b => new { b.VaultId, b.CreatedAt });

        // Không cascade từ kho: dữ liệu kho không bị xóa dây chuyền.
        builder.HasOne(b => b.Vault)
            .WithMany()
            .HasForeignKey(b => b.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
