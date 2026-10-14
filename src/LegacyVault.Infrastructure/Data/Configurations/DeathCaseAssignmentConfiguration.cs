using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseAssignmentConfiguration : IEntityTypeConfiguration<DeathCaseAssignment>
{
    public void Configure(EntityTypeBuilder<DeathCaseAssignment> builder)
    {
        builder.ToTable("DeathCaseAssignments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EndReason)
            .HasMaxLength(32)
            .IsUnicode(false);

        // D4: mỗi hồ sơ chỉ một phân công còn hiệu lực.
        builder.HasIndex(a => a.CaseId)
            .IsUnique()
            .HasFilter("[EndedAt] IS NULL")
            .HasDatabaseName("UX_DeathCaseAssignments_ActivePerCase");

        // ASSIGN-01: tìm lần được giao gần nhất của từng Verifier để xoay vòng.
        builder.HasIndex(a => new { a.VerifierUserId, a.AssignedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.VerifierUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
