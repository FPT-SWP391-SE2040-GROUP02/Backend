using LegacyVault.Domain.Entities;

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
}
