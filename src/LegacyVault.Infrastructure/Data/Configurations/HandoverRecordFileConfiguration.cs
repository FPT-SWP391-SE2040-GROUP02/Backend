using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverRecordFileConfiguration : IEntityTypeConfiguration<HandoverRecordFile>
{
    public void Configure(EntityTypeBuilder<HandoverRecordFile> builder)
    {
        builder.ToTable("HandoverRecordFiles");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(f => f.FileName).IsRequired().HasMaxLength(255);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(64);
        builder.Property(f => f.StorageKey).IsRequired().HasMaxLength(256);
        builder.Property(f => f.EncryptionKeyId).IsRequired().HasMaxLength(64);
        builder.Property(f => f.WrappedDataKey).IsRequired();

        builder.HasIndex(f => new { f.RecordId, f.ClaimId });
    }
}
