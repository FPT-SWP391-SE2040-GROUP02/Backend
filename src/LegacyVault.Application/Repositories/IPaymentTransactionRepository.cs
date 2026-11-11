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
        PaymentTransactionStatus? status, string? search, int pageIndex, int pageSize, CancellationToken ct,
        PaymentTransactionFilter? filter = null);

    /// <summary>
    /// Thời điểm nhận và số tiền của các giao dịch thành công (Matched và Resolved), lọc theo môi trường và khoảng thời gian
    /// của filter (SuccessOnly và status không dùng). Chỉ đọc, không theo dõi thay đổi, không phân trang: dùng để cộng doanh thu.
    /// </summary>
    Task<List<RevenueTransaction>> ListSuccessAmountsAsync(PaymentTransactionFilter filter, CancellationToken ct);
}

/// <summary>Một giao dịch thành công rút gọn cho việc cộng doanh thu.</summary>
public sealed class RevenueTransaction
{
    public DateTimeOffset ReceivedAt { get; set; }

    public long Amount { get; set; }
}

/// <summary>Bộ lọc thêm của danh sách giao dịch cho Admin. Mốc thời gian lọc theo cột "Nhận lúc" (ReceivedAt).</summary>
public sealed class PaymentTransactionFilter
{
    /// <summary>true: chỉ giao dịch thành công, gồm Matched và Resolved; khi bật thì bỏ qua tham số status.</summary>
    public bool SuccessOnly { get; set; }

    /// <summary>"Live" hoặc "Test"; null là mọi môi trường.</summary>
    public string? Environment { get; set; }

    /// <summary>Từ thời điểm này, tính cả mốc.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Trước thời điểm này, không tính mốc.</summary>
    public DateTimeOffset? ToExclusive { get; set; }
}
