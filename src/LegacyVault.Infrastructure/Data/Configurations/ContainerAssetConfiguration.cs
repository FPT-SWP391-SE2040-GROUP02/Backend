using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ContainerAssetConfiguration : IEntityTypeConfiguration<ContainerAsset>
{
    public void Configure(EntityTypeBuilder<ContainerAsset> builder)
    {
        builder.ToTable("ContainerAssets");
        builder.HasKey(l => l.Id);

        // SETUP-03 / DATA-04: một tài sản thuộc tối đa một Container — chặn ở DB.
        builder.HasIndex(l => l.AssetId).IsUnique();

        // Restrict: tránh hai đường cascade (Vault→Container→Link và Vault→Asset→Link) mà SQL Server không cho.
        builder.HasOne(l => l.Asset).WithMany()
            .HasForeignKey(l => l.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
