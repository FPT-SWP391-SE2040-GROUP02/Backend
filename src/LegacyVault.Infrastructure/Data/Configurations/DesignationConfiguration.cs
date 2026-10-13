using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DesignationConfiguration : IEntityTypeConfiguration<Designation>
{
    public void Configure(EntityTypeBuilder<Designation> builder)
    {
        builder.ToTable("Designations");

        builder.HasKey(d => d.Id);

        // Một hồ sơ chỉ được chỉ định một lần trong một gói; chặn ở DB khi bấm lặp/gửi đồng thời.
        builder.HasIndex(d => new { d.ContainerId, d.BeneficiaryId })
            .IsUnique();

        // Tìm các gói của một hồ sơ (containerIds, chặn xóa hồ sơ đang dùng, đếm suất QUOTA-03).
        builder.HasIndex(d => d.BeneficiaryId);

        // Restrict cả hai phía: gói chỉ lưu trữ (không xóa hẳn); hồ sơ đang có chỉ định thì không xóa được.
        builder.HasOne(d => d.Container)
            .WithMany(c => c.Designations)
            .HasForeignKey(d => d.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Beneficiary)
            .WithMany()
            .HasForeignKey(d => d.BeneficiaryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
