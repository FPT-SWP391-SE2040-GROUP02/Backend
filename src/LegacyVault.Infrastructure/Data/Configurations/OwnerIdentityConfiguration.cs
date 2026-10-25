using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class OwnerIdentityConfiguration : IEntityTypeConfiguration<OwnerIdentity>
{
    public void Configure(EntityTypeBuilder<OwnerIdentity> builder)
    {
        builder.ToTable("OwnerIdentities");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.CitizenIdLast4)
            .IsRequired()
            .HasMaxLength(4)
            .IsUnicode(false)
            .IsFixedLength();

        builder.Property(i => i.EncryptionKeyId)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        // Giống Beneficiary: nonce 12 + DEK 32 + tag 16 = 60 byte (DataKeyProtector.WrappedKeySize).
        builder.Property(i => i.WrappedDataKey)
            .IsRequired()
            .HasMaxLength(60)
            .IsFixedLength();

        builder.Property(i => i.IdentityEncrypted)
            .IsRequired();

        // Ảnh mặt trước CCCD (BR-48 sửa): cùng kiểu cột tham chiếu với ảnh đại diện của Users.
        builder.Property(i => i.DocumentImageStorageKey).HasMaxLength(200).IsUnicode(false);
        builder.Property(i => i.DocumentImageContentType).HasMaxLength(50).IsUnicode(false);
        builder.Property(i => i.DocumentImageSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(i => i.DocumentImageEncryptionKeyId).HasMaxLength(64).IsUnicode(false);
        builder.Property(i => i.DocumentImageWrappedDataKey).HasMaxLength(60).IsFixedLength();

        // Mỗi tài khoản một danh tính đã gắn (EKYC-24); hai request đăng ký cùng lúc không tạo hai dòng.
        builder.HasIndex(i => i.UserId)
            .IsUnique();

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
