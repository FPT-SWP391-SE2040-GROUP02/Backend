using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HandoverSnapshotItemConfiguration : IEntityTypeConfiguration<HandoverSnapshotItem>
{
    public void Configure(EntityTypeBuilder<HandoverSnapshotItem> builder)
    {
        builder.ToTable("HandoverSnapshotItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(i => i.FileName)
            .HasMaxLength(255);

        builder.Property(i => i.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(i => new { i.TurnId, i.Position });
    }
}
