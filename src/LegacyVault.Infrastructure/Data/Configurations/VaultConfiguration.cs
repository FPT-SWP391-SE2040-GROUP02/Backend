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

        builder.Property(v => v.Description)
            .HasMaxLength(2000);

        builder.Property(v => v.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

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

        builder.HasMany(v => v.Containers)
            .WithOne(c => c.Vault)
            .HasForeignKey(c => c.VaultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
