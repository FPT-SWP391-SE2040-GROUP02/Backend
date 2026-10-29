using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseObjectionConfiguration : IEntityTypeConfiguration<DeathCaseObjection>
{
    public void Configure(EntityTypeBuilder<DeathCaseObjection> builder)
    {
        builder.ToTable("DeathCaseObjections");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.PreviousCaseStatus).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.PreviousPlanStatus).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.Conclusion).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.ConclusionReason).HasMaxLength(1000);

        // Ảnh selfie gần nhất (BR-50 sửa): cùng kiểu cột tham chiếu với ảnh đại diện của Users.
        builder.Property(o => o.SelfieImageStorageKey).HasMaxLength(200).IsUnicode(false);
        builder.Property(o => o.SelfieImageContentType).HasMaxLength(50).IsUnicode(false);
        builder.Property(o => o.SelfieImageEncryptionKeyId).HasMaxLength(64).IsUnicode(false);
        builder.Property(o => o.SelfieImageWrappedDataKey).HasMaxLength(60).IsFixedLength();

        builder.Property(o => o.UpdatedAt).IsConcurrencyToken();

        // ALIVE-09: mỗi hồ sơ tối đa một báo đang mở; hai lần báo đầu cùng lúc chỉ tạo một dòng.
        builder.HasIndex(o => o.CaseId)
            .IsUnique()
            .HasFilter("[Status] = 'Open'");

        builder.HasIndex(o => new { o.VerifierUserId, o.Status });
        builder.HasIndex(o => o.VaultId);

        builder.HasOne<DeathCase>()
            .WithMany()
            .HasForeignKey(o => o.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
