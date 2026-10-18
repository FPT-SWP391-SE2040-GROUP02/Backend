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

    /// <summary>
    /// Tạo đơn Pending với giá đã chốt (PAY-11). Đã có đơn Pending còn hạn cùng lựa chọn → trả lại đơn cũ (Created = false);
    /// khác lựa chọn → 409 PAYMENT_ORDER_PENDING (PAY-04). Đơn Pending cũ đã quá hạn được đổi Expired trong cùng lần lưu.
    /// </summary>
    Task<CreatePaymentOrderResult> CreateOrderAsync(BillingSelectionRequest request, CancellationToken ct);

    /// <summary>Đơn Pending còn hạn của kho; không có (hoặc vừa hết hạn) → null.</summary>
    Task<PaymentOrderResponse?> GetCurrentOrderAsync(CancellationToken ct);

    /// <summary>Một đơn của kho mình; đơn không tồn tại hoặc của người khác → 404 PAYMENT_ORDER_NOT_FOUND.</summary>
    Task<PaymentOrderResponse> GetOrderAsync(Guid orderId, CancellationToken ct);

    /// <summary>Hủy đơn Pending; hủy lặp trả lại đơn đã hủy; đơn Paid hoặc Expired → 409 PAYMENT_ORDER_NOT_PAYABLE.</summary>
    Task<PaymentOrderResponse> CancelOrderAsync(Guid orderId, CancellationToken ct);
}
