namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một dòng lịch sử thanh toán (GET /api/v1/billing/orders). Gọn hơn <see cref="PaymentOrderResponse"/>: không có
/// thông tin ngân hàng hay mã QR. Đơn Pending đã quá hạn được hiện là EXPIRED (chỉ khi đọc, không ghi DB).
/// </summary>
public class PaymentOrderSummaryResponse
{
    public Guid Id { get; set; }

    /// <summary>PURCHASE, ADDON, UPGRADE hoặc RENEWAL.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>PENDING, PAID, EXPIRED hoặc CANCELLED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Hạng của đơn (đơn ADDON là hạng hiện tại của kho).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Số năm; null với đơn ADDON và UPGRADE.</summary>
    public int? Years { get; set; }

    public int ExtraSlots { get; set; }

    public long TotalAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Mã thanh toán "LV..." cũng là nội dung chuyển khoản; dùng làm mã tham chiếu đơn.</summary>
    public string PaymentCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }
}
