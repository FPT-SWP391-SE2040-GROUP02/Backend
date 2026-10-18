using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class DeathCaseFlagConfiguration : IEntityTypeConfiguration<DeathCaseFlag>
{
    public void Configure(EntityTypeBuilder<DeathCaseFlag> builder)
    {
        builder.ToTable("DeathCaseFlags");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(f => f.Detail)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(f => f.ReviewNote)
            .HasMaxLength(1000);

        // DEATH-10: mỗi loại cờ chỉ một dòng mỗi phiên bản.
        builder.HasIndex(f => new { f.VersionId, f.Kind })
            .IsUnique();

        builder.HasOne<DeathCaseVersion>()
            .WithMany()
            .HasForeignKey(f => f.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
