using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class VerifierSigningKeyConfiguration : IEntityTypeConfiguration<VerifierSigningKey>
{
    public void Configure(EntityTypeBuilder<VerifierSigningKey> builder)
    {
        builder.ToTable("VerifierSigningKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Algorithm)
            .IsRequired()
            .HasMaxLength(32);

        // SubjectPublicKeyInfo của RSA-2048 khoảng 294 byte.
        builder.Property(k => k.PublicKey)
            .IsRequired()
            .HasMaxLength(512);

        // Giống AssetConfiguration: id KEK và DEK đã bọc (12 nonce + 32 khóa + 16 tag = 60 byte).
        builder.Property(k => k.EncryptionKeyId)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);
        builder.Property(k => k.WrappedDataKey)
            .IsRequired()
            .HasMaxLength(60)
            .IsFixedLength();

        // PKCS#8 của RSA-2048 khoảng 1,2 KB, cộng 28 byte nonce + tag.
        builder.Property(k => k.PrivateKeyEncrypted)
            .IsRequired()
            .HasMaxLength(4000);

        // DEATH-14: mỗi Verifier tối đa một khóa đang dùng; khóa đã thu hồi không chặn khóa mới.
        builder.HasIndex(k => k.UserId, "UX_VerifierSigningKeys_ActivePerUser")
            .IsUnique()
            .HasFilter("[RevokedAt] IS NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
