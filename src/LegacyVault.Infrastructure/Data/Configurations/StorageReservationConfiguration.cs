using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class StorageReservationConfiguration : IEntityTypeConfiguration<StorageReservation>
{
    public void Configure(EntityTypeBuilder<StorageReservation> builder)
    {
        builder.ToTable("StorageReservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(32);

        // Tính tổng phần đang giữ chỗ của kho.
        builder.HasIndex(r => new { r.VaultId, r.Status, r.ExpiresAt });
    }
}
