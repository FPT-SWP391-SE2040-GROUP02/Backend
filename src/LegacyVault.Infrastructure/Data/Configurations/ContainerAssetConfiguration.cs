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

        // DATA-04: Mỗi tài sản chỉ thuộc tối đa một Container chưa lưu trữ
        builder.HasIndex(a => a.AssetId)
            .IsUnique();

        builder.HasOne(a => a.Container)
            .WithMany(c => c.Assets)
            .HasForeignKey(a => a.ContainerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
