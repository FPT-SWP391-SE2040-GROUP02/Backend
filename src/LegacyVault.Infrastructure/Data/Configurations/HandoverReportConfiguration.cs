using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverReportConfiguration : IEntityTypeConfiguration<HandoverReport>
{
    public void Configure(EntityTypeBuilder<HandoverReport> builder)
    {
        builder.ToTable("HandoverReports");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Note).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.ReviewReason).HasMaxLength(1000);
        builder.Property(r => r.AcceptNote).HasMaxLength(1000);
        builder.Property(r => r.DataProtected).IsRequired();
        builder.Property(r => r.UpdatedAt).IsConcurrencyToken();

        builder.HasIndex(r => r.SubmitterUserId);
        builder.HasIndex(r => new { r.VerifierUserId, r.Status });
        builder.HasIndex(r => r.RecordId);

        builder.HasOne<HandoverRecord>().WithMany().HasForeignKey(r => r.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vault>().WithMany().HasForeignKey(r => r.VaultId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.SubmitterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.VerifierUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Files).WithOne(f => f.Report).HasForeignKey(f => f.ReportId).OnDelete(DeleteBehavior.Cascade);
    }
}
