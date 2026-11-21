using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverRecordConfiguration : IEntityTypeConfiguration<HandoverRecord>
{
    public void Configure(EntityTypeBuilder<HandoverRecord> builder)
    {
        builder.ToTable("HandoverRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.UpdatedAt)
            .IsConcurrencyToken();

        // Mỗi kho chỉ có một biên bản bàn giao.
        builder.HasIndex(r => r.VaultId)
            .IsUnique();
        builder.HasIndex(r => new { r.VerifierUserId, r.Status });

        builder.HasOne<Vault>()
            .WithMany()
            .HasForeignKey(r => r.VaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Lines)
            .WithOne(l => l.Record)
            .HasForeignKey(l => l.RecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Signatures)
            .WithOne(s => s.Record)
            .HasForeignKey(s => s.RecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
