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
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);

        // ETag của gói (UpdateContainerRequest.Etag), giống Vault.
        builder.Property(c => c.UpdatedAt).IsConcurrencyToken();
        builder.HasIndex(c => new { c.VaultId, c.Status });

        // Không cascade từ kho: dữ liệu kho không bị xóa dây chuyền.
        builder.HasOne(c => c.Vault).WithMany(v => v.Containers)
            .HasForeignKey(c => c.VaultId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Assets).WithOne(a => a.Container)
            .HasForeignKey(a => a.ContainerId).OnDelete(DeleteBehavior.Cascade);
    }
}
