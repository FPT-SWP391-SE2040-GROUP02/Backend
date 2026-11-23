using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class AssetAssignmentConfiguration : IEntityTypeConfiguration<AssetAssignment>
{
    public void Configure(EntityTypeBuilder<AssetAssignment> builder)
    {
        builder.ToTable("AssetAssignments");

        builder.HasKey(a => a.Id);

        // Một tài sản chỉ gán cho một người nhận đúng một lần; chặn ở cơ sở dữ liệu khi bấm lặp hoặc gửi đồng thời.
        builder.HasIndex(a => new { a.AssetId, a.BeneficiaryId })
            .IsUnique();

        // Tìm các tài sản của một người nhận (chặn xóa người nhận đang được gán, đếm suất người nhận).
        builder.HasIndex(a => a.BeneficiaryId);

        // Restrict cả hai phía: tài sản đang được gán thì không xóa được; người nhận đang được gán thì không xóa được.
        builder.HasOne(a => a.Asset)
            .WithMany()
            .HasForeignKey(a => a.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Beneficiary)
            .WithMany()
            .HasForeignKey(a => a.BeneficiaryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
