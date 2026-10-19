using LegacyVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Data.Configurations;

public sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Provider)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(t => t.Environment)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(t => t.Gateway)
            .HasMaxLength(64);

        builder.Property(t => t.AccountNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(t => t.Code)
            .HasMaxLength(32);

        builder.Property(t => t.MatchedPaymentCode)
            .HasMaxLength(16);

        builder.Property(t => t.Content)
            .HasMaxLength(512);

        builder.Property(t => t.TransferType)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(t => t.ReferenceCode)
            .HasMaxLength(64);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(t => t.ReconciliationReason)
            .HasMaxLength(32);

        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        // AC-29, PAY-05: webhook gửi lặp cùng ID giao dịch không tạo thêm dòng, kể cả khi hai request chạy song song.
        builder.HasIndex(t => new { t.Provider, t.ProviderTransactionId })
            .IsUnique();

        // PAY-04: một đơn chỉ có tối đa một giao dịch Matched; giao dịch lệch của cùng đơn vẫn được lưu.
        // Hai chỉ mục cùng cột PaymentOrderId nên phải đặt tên ngay ở HasIndex (overload có name), nếu không EF gộp thành một.
        builder.HasIndex(t => t.PaymentOrderId, "IX_PaymentTransactions_PaymentOrderId_Matched")
            .IsUnique()
            .HasFilter("[Status] = 'Matched'");

        // Tra cứu giao dịch theo đơn.
        builder.HasIndex(t => t.PaymentOrderId, "IX_PaymentTransactions_PaymentOrderId");

        // Admin lọc theo trạng thái, mới nhất trước.
        builder.HasIndex(t => new { t.Status, t.ReceivedAt });

        builder.HasOne<PaymentOrder>()
            .WithMany()
            .HasForeignKey(t => t.PaymentOrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
