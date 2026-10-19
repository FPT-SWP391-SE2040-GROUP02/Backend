using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng PaymentTransactions.</summary>
public interface IPaymentTransactionRepository
{
    /// <summary>Giao dịch theo (nhà cung cấp, ID giao dịch của nhà cung cấp), có theo dõi thay đổi. Không có → null.</summary>
    Task<PaymentTransaction?> FindByProviderTransactionIdAsync(string provider, long providerTransactionId, CancellationToken ct);

    /// <summary>
    /// Thêm giao dịch rồi lưu NGAY (PAY-05: lưu trước khi đối chiếu). Trả false khi DB từ chối vì vi phạm unique
    /// (Provider, ProviderTransactionId): webhook gửi lặp hoặc hai request song song cùng giao dịch (AC-29).
    /// </summary>
    Task<bool> TryAddAsync(PaymentTransaction transaction, CancellationToken ct);

    /// <summary>
    /// Một trang giao dịch cho Admin, mới nhận trước, lọc theo trạng thái nếu có (null = tất cả). Chỉ đọc, không theo dõi
    /// thay đổi. pageIndex bắt đầu từ 1. Trả (các dòng của trang, tổng số dòng sau khi lọc).
    /// </summary>
    Task<(List<PaymentTransaction> Items, int Total)> ListAsync(
        PaymentTransactionStatus? status, int pageIndex, int pageSize, CancellationToken ct);
}
