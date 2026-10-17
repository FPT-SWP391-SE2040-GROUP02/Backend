using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class HoldConfiguration : IEntityTypeConfiguration<Hold>
{
    public void Configure(EntityTypeBuilder<Hold> builder)
    {
        builder.ToTable("Holds");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Scope).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.Type).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.Reason).IsRequired().HasMaxLength(500);
        builder.Property(h => h.ReleaseReason).HasMaxLength(500);
        builder.Property(h => h.UpdatedAt).IsConcurrencyToken();

        // Kiểm hold đang hiệu lực của kho ở mọi thao tác phát hành.
        builder.HasIndex(h => new { h.VaultId, h.Status });
        builder.HasIndex(h => new { h.Status, h.ReviewAt });
        // Mỗi hold F2C một dòng đang mở cho mỗi (kho, loại, đối tượng): đặt hai lần cùng lúc không tạo trùng.
        builder.HasIndex(h => new { h.VaultId, h.Type, h.Scope, h.TargetId })
            .IsUnique()
            .HasFilter("[Status] = 'Active' AND [Type] = 'OwnerAlive'");

        builder.HasOne<Vault>()
            .WithMany()
            .HasForeignKey(h => h.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
