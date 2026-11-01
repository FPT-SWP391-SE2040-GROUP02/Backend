using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class VaultConfiguration : IEntityTypeConfiguration<Vault>
{
    public void Configure(EntityTypeBuilder<Vault> builder)
    {
        builder.ToTable("Vaults");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Name)
            .IsRequired()
            .HasMaxLength(200);

        // Khớp UpdateVaultRequest/CreateVaultRequest (tối đa 1000 ký tự).
        builder.Property(v => v.Description)
            .HasMaxLength(1000);

        // Khớp UpdateVaultRequest.ExecutorNote (tối đa 2000 ký tự).
        builder.Property(v => v.ExecutorNote)
            .HasMaxLength(2000);

        builder.Property(v => v.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        // ETag: UPDATE ... WHERE UpdatedAt = giá trị đã đọc; lệch → EDIT_CONFLICT.
        builder.Property(v => v.UpdatedAt)
            .IsConcurrencyToken();

        // SETUP-12 / DATA-04: Mỗi OwnerId chỉ có tối đa một kho trong toàn hệ thống
        builder.HasIndex(v => v.OwnerId)
            .IsUnique();

        builder.HasOne(v => v.Owner)
            .WithMany()
            .HasForeignKey(v => v.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Plan)
            .WithOne(p => p.Vault)
            .HasForeignKey<HandoverPlan>(p => p.VaultId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Entitlement)
            .WithOne(e => e.Vault)
            .HasForeignKey<VaultEntitlement>(e => e.VaultId)
            .OnDelete(DeleteBehavior.Cascade);

        // Quan hệ Vault → Containers khai báo ở ContainerConfiguration (Restrict).
    }
}
