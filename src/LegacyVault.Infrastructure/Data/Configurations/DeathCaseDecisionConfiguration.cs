using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseDecisionConfiguration : IEntityTypeConfiguration<DeathCaseDecision>
{
    public void Configure(EntityTypeBuilder<DeathCaseDecision> builder)
    {
        builder.ToTable("DeathCaseDecisions");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(d => d.Reason)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(d => d.RequestedParts)
            .HasMaxLength(256);

        // Tra quyết định của một hồ sơ (kể cả các lần yêu cầu bổ sung). Đặt tên để EF không gộp với chỉ mục unique bên dưới.
        builder.HasIndex(d => d.CaseId, "IX_DeathCaseDecisions_CaseId");

        // AC-71, DEATH-03: mỗi hồ sơ tối đa một quyết định cuối (từ chối hoặc duyệt);
        // yêu cầu bổ sung thì được nhiều dòng.
        builder.HasIndex(d => d.CaseId, "UX_DeathCaseDecisions_FinalPerCase")
            .IsUnique()
            .HasFilter("[Kind] IN ('Rejected','Approved')");

        // Giữ lịch sử quyết định: không xóa dây chuyền từ hồ sơ, người dùng, phân công.
        builder.HasOne<DeathCase>()
            .WithMany(c => c.Decisions)
            .HasForeignKey(d => d.CaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.VerifierUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DeathCaseAssignment>()
            .WithMany()
            .HasForeignKey(d => d.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
