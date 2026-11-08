using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverSubmissionConfiguration : IEntityTypeConfiguration<HandoverSubmission>
{
    public void Configure(EntityTypeBuilder<HandoverSubmission> builder)
    {
        builder.ToTable("HandoverSubmissions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.CitizenIdLast4).IsRequired().HasMaxLength(4);
        builder.Property(s => s.SupplementReason).HasMaxLength(1000);

        builder.Property(s => s.FrontImageStorageKey).IsRequired().HasMaxLength(256);
        builder.Property(s => s.FrontImageContentType).IsRequired().HasMaxLength(64);
        builder.Property(s => s.FrontImageEncryptionKeyId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.FrontImageWrappedDataKey).IsRequired();
        builder.Property(s => s.BackImageStorageKey).IsRequired().HasMaxLength(256);
        builder.Property(s => s.BackImageContentType).IsRequired().HasMaxLength(64);
        builder.Property(s => s.BackImageEncryptionKeyId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.BackImageWrappedDataKey).IsRequired();

        builder.Property(s => s.PaperStorageKey).IsRequired().HasMaxLength(256).HasDefaultValue(string.Empty);
        builder.Property(s => s.PaperEncryptionKeyId).IsRequired().HasMaxLength(64).HasDefaultValue(string.Empty);
        builder.Property(s => s.PaperWrappedDataKey).IsRequired().HasDefaultValue(Array.Empty<byte>());

        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasOne(s => s.Turn)
            .WithMany()
            .HasForeignKey(s => s.TurnId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tìm hồ sơ mới nhất của một lượt, và hàng chờ duyệt theo trạng thái.
        builder.HasIndex(s => new { s.TurnId, s.CreatedAt });
        builder.HasIndex(s => s.Status);
    }
}
