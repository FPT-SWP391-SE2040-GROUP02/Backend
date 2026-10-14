using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class ContainerConfiguration : IEntityTypeConfiguration<Container>
{
    public void Configure(EntityTypeBuilder<Container> builder)
    {
        builder.ToTable("Containers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Khớp CreateContainerRequest/UpdateContainerRequest (tối đa 1000 ký tự).
        builder.Property(c => c.Description)
            .HasMaxLength(1000);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // ETag của gói (UpdateContainerRequest.Etag), giống Vault.
        builder.Property(c => c.UpdatedAt)
            .IsConcurrencyToken();

        builder.HasIndex(c => new { c.VaultId, c.Status });

        // Không cascade từ kho: dữ liệu kho không bị xóa dây chuyền.
        // Quan hệ Container → ContainerAssets khai báo ở ContainerAssetConfiguration.
        builder.HasOne(c => c.Vault)
            .WithMany(v => v.Containers)
            .HasForeignKey(c => c.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
