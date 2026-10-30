using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ContainerAssetConfiguration : IEntityTypeConfiguration<ContainerAsset>
{
    public void Configure(EntityTypeBuilder<ContainerAsset> builder)
    {
        builder.ToTable("ContainerAssets");

        builder.HasKey(a => a.Id);

        // DATA-04: Mỗi tài sản chỉ thuộc tối đa một Container chưa bị xóa
        builder.HasIndex(a => a.AssetId)
            .IsUnique();

        builder.HasOne(a => a.Container)
            .WithMany(c => c.Assets)
            .HasForeignKey(a => a.ContainerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: tránh hai đường cascade (Vault→Container→Link và Vault→Asset→Link) mà SQL Server không cho.
        builder.HasOne(a => a.Asset)
            .WithMany()
            .HasForeignKey(a => a.AssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
