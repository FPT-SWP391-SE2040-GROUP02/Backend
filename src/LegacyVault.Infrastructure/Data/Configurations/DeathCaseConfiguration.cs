using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseConfiguration : IEntityTypeConfiguration<DeathCase>
{
    public void Configure(EntityTypeBuilder<DeathCase> builder)
    {
        builder.ToTable("DeathCases");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // D2: trạng thái kế hoạch trước khi gửi lần đầu, lưu dạng chữ như HandoverPlans.Status.
        builder.Property(c => c.StatusBeforeCase)
            .HasConversion<string>()
            .HasMaxLength(32);

        // ETag của hồ sơ; hai request cùng sửa thì bên lưu sau bị 409 EDIT_CONFLICT.
        builder.Property(c => c.UpdatedAt)
            .IsConcurrencyToken();

        // DEATH-05, DATA-04, D1: mỗi kho chỉ một hồ sơ chưa bị từ chối (kể cả Draft) — AC-71, AC-91.
        builder.HasIndex(c => c.VaultId)
            .IsUnique()
            .HasFilter("[Status] <> 'Rejected'")
            .HasDatabaseName("UX_DeathCases_OpenPerVault");

        builder.HasIndex(c => c.PlanId);

        // Giữ lịch sử: không xóa dây chuyền từ kho, kế hoạch, người dùng.
        builder.HasOne<Vault>()
            .WithMany()
            .HasForeignKey(c => c.VaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<HandoverPlan>()
            .WithMany()
            .HasForeignKey(c => c.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.OpenedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DeathCase>()
            .WithMany()
            .HasForeignKey(c => c.PreviousCaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Versions)
            .WithOne()
            .HasForeignKey(v => v.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Flags)
            .WithOne()
            .HasForeignKey(f => f.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Notices)
            .WithOne()
            .HasForeignKey(n => n.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Assignments)
            .WithOne()
            .HasForeignKey(a => a.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
