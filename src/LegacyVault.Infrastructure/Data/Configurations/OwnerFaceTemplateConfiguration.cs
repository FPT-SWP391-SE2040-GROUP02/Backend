using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class OwnerFaceTemplateConfiguration : IEntityTypeConfiguration<OwnerFaceTemplate>
{
    public void Configure(EntityTypeBuilder<OwnerFaceTemplate> builder)
    {
        builder.ToTable("OwnerFaceTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ModelName)
            .IsRequired()
            .HasMaxLength(32)
            .IsUnicode(false);

        builder.Property(t => t.EncryptionKeyId)
            .IsRequired()
            .HasMaxLength(64)
            .IsUnicode(false);

        // Giống Beneficiary: nonce 12 + DEK 32 + tag 16 = 60 byte (DataKeyProtector.WrappedKeySize).
        builder.Property(t => t.WrappedDataKey)
            .IsRequired()
            .HasMaxLength(60)
            .IsFixedLength();

        builder.Property(t => t.EmbeddingEncrypted)
            .IsRequired();

        // Mỗi người một dãy phiên bản; hai request đăng ký cùng lúc không tạo hai mẫu cùng phiên bản (EKYC-23).
        builder.HasIndex(t => new { t.UserId, t.Version })
            .IsUnique();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
