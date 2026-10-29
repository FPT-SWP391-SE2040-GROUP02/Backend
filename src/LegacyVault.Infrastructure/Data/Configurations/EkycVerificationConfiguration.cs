using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class EkycVerificationConfiguration : IEntityTypeConfiguration<EkycVerification>
{
    public void Configure(EntityTypeBuilder<EkycVerification> builder)
    {
        builder.ToTable("EkycVerifications");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(v => v.Purpose)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(v => v.IdentityMatch)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // Rỗng khi OCR không đọc được số; còn lại đúng 4 chữ số.
        builder.Property(v => v.CitizenIdLast4)
            .IsRequired()
            .HasMaxLength(4)
            .IsUnicode(false);

        // secrets.token_urlsafe(24) của dịch vụ eKYC dài 32 ký tự.
        builder.Property(v => v.DocumentToken)
            .HasMaxLength(64)
            .IsUnicode(false);

        // Danh tính tạm (mã hóa) của lượt OWNER_ENROLLMENT đang chờ selfie; null ở các lượt khác và sau khi chốt.
        builder.Property(v => v.EnrollmentIdentityKeyId)
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(v => v.EnrollmentWrappedDataKey)
            .HasMaxLength(60)
            .IsFixedLength();

        // Ảnh mặt trước CCCD tạm của lượt OWNER_ENROLLMENT (BR-48 sửa); cùng kiểu cột với ảnh đại diện của Users.
        builder.Property(v => v.PendingDocumentImageStorageKey).HasMaxLength(200).IsUnicode(false);
        builder.Property(v => v.PendingDocumentImageContentType).HasMaxLength(50).IsUnicode(false);
        builder.Property(v => v.PendingDocumentImageEncryptionKeyId).HasMaxLength(64).IsUnicode(false);
        builder.Property(v => v.PendingDocumentImageWrappedDataKey).HasMaxLength(60).IsFixedLength();

        // Mã phiên của dịch vụ eKYC, cùng kiểu với DocumentToken.
        builder.Property(v => v.LivenessSessionId)
            .HasMaxLength(64)
            .IsUnicode(false);

        // Tìm lượt mới nhất của một người dùng.
        builder.HasIndex(v => new { v.UserId, v.CreatedAt });

        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
