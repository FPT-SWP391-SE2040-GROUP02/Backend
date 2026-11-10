using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverDocumentConfiguration : IEntityTypeConfiguration<HandoverDocument>
{
    public void Configure(EntityTypeBuilder<HandoverDocument> builder)
    {
        builder.ToTable("HandoverDocuments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.TemplateVersion).IsRequired().HasMaxLength(32);
        builder.Property(d => d.OwnerFullName).IsRequired().HasMaxLength(200);
        builder.Property(d => d.CertificateNumber).IsRequired().HasMaxLength(100);
        builder.Property(d => d.RecipientFullName).IsRequired().HasMaxLength(200);
        builder.Property(d => d.RecipientCitizenIdLast4).IsRequired().HasMaxLength(4);
        builder.Property(d => d.RecipientTermsVersion).IsRequired().HasMaxLength(32);
        builder.Property(d => d.SupplementReason).HasMaxLength(1000);
        builder.Property(d => d.VerifierChecks).IsRequired().HasMaxLength(8);
        builder.Property(d => d.VerifierTermsVersion).IsRequired().HasMaxLength(32);
        builder.Property(d => d.RowVersion).IsRowVersion();

        // Một lượt chỉ có một biên bản. Xóa lượt (dọn theo thời hạn lưu) không xóa biên bản, nên không cascade.
        builder.HasOne(d => d.Turn)
            .WithMany()
            .HasForeignKey(d => d.TurnId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.TurnId).IsUnique();

        // Danh sách theo kho (Người thực thi xem lại, Người xét duyệt) và theo trạng thái.
        builder.HasIndex(d => new { d.VaultId, d.CreatedAt });
    }
}
