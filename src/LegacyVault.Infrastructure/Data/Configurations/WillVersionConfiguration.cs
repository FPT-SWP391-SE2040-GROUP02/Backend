using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class WillVersionConfiguration : IEntityTypeConfiguration<WillVersion>
{
    public void Configure(EntityTypeBuilder<WillVersion> builder)
    {
        builder.ToTable("WillVersions");

        builder.HasKey(w => w.Id);

        // Mỗi kho có các phiên bản đánh số liên tục; hai request ký song song không tạo trùng số.
        builder.HasIndex(w => new { w.VaultId, w.VersionNo })
            .IsUnique();

        builder.Property(w => w.SnapshotJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.HasOne<Vault>()
            .WithMany()
            .HasForeignKey(w => w.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
