using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Thanh toán và quyền dịch vụ (SRS §8, luồng P). Kho luôn lấy từ người đang đăng nhập, không nhận vaultId từ client.</summary>
public interface IBillingService
{
    /// <summary>Bảng giá hiện hành. Không đọc DB.</summary>
    Task<BillingPlansResponse> GetPlansAsync(CancellationToken ct);

    /// <summary>
    /// Báo giá cho kho của người đang đăng nhập (D2): kiểm điều kiện như khi tạo đơn rồi tính giá, không ghi DB.
    /// </summary>
    Task<BillingQuoteResponse> QuoteAsync(BillingSelectionRequest request, CancellationToken ct);
}
