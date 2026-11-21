using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverRecordSignatureConfiguration : IEntityTypeConfiguration<HandoverRecordSignature>
{
    public void Configure(EntityTypeBuilder<HandoverRecordSignature> builder)
    {
        builder.ToTable("HandoverRecordSignatures");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(s => new { s.RecordId, s.RevisionNo });
    }
}
