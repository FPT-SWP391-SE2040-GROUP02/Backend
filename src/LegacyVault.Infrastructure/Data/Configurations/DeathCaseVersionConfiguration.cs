using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseVersionConfiguration : IEntityTypeConfiguration<DeathCaseVersion>
{
    public void Configure(EntityTypeBuilder<DeathCaseVersion> builder)
    {
        builder.ToTable("DeathCaseVersions");

        builder.HasKey(v => v.Id);

        // DEATH-04: số phiên bản không trùng trong một hồ sơ.
        builder.HasIndex(v => new { v.CaseId, v.VersionNo })
            .IsUnique();

        builder.Property(v => v.EncryptionKeyId)
            .HasMaxLength(64)
            .IsUnicode(false);

        // Giống Beneficiary: nonce 12 + DEK 32 + tag 16 = 60 byte.
        builder.Property(v => v.WrappedDataKey)
            .HasMaxLength(60)
            .IsFixedLength();

        builder.Property(v => v.OwnerCitizenIdLast4)
            .HasMaxLength(4)
            .IsUnicode(false)
            .IsFixedLength();

        builder.HasMany(v => v.Files)
            .WithOne()
            .HasForeignKey(f => f.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
