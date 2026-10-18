using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ActorRole)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(l => l.Action)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(l => l.TargetType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(l => l.Outcome)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(l => l.Reason)
            .HasMaxLength(500);

        // SEC-13: Owner xem nhật ký kho mình, mới nhất trước, chia trang ổn định theo Id.
        builder.HasIndex(l => new { l.VaultId, l.At, l.Id });

        // ADMIN-06: lọc theo người thực hiện.
        builder.HasIndex(l => new { l.ActorUserId, l.At });

        // DATA-07: nhật ký giữ lịch sử, không xóa dây chuyền từ kho hay tài khoản.
        builder.HasOne<Vault>()
            .WithMany()
            .HasForeignKey(l => l.VaultId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(l => l.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
