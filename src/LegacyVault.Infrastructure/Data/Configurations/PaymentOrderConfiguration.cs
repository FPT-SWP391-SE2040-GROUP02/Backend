using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        builder.ToTable("PaymentOrders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(o => o.Tier)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(o => o.PolicyVersion)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(o => o.PaymentCode)
            .IsRequired()
            .HasMaxLength(16);

        // Hai lần ghi nhận thanh toán song song: RowVersion lệch thì bên sau nhận 409, không cấp quyền hai lần.
        builder.Property(o => o.RowVersion)
            .IsRowVersion();

        // Mã chuyển khoản không trùng giữa các đơn.
        builder.HasIndex(o => o.PaymentCode)
            .IsUnique();

        // PAY-04: mỗi kho tối đa một đơn đang chờ thanh toán, kể cả khi hai request tạo đơn chạy song song.
        builder.HasIndex(o => o.VaultId)
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("IX_PaymentOrders_VaultId_Pending");

        // Xem lịch sử đơn của kho.
        builder.HasIndex(o => new { o.VaultId, o.CreatedAt });

        builder.HasOne<Vault>()
            .WithMany()
            .HasForeignKey(o => o.VaultId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
