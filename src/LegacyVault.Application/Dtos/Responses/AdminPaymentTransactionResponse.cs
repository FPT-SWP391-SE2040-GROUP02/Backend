namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một giao dịch tiền vào cho màn đối chiếu của Admin (SRS ADMIN-09): ID giao dịch, đơn, số tiền, kết quả đối chiếu, lỗi.
/// Không có nội dung chuyển khoản, số tài khoản, key hay RowVersion (SEC-07, AC-75).
/// </summary>
public class AdminPaymentTransactionResponse
{
    public Guid Id { get; set; }

    /// <summary>"SePay".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>"Test" hoặc "Live".</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ID giao dịch phía SePay.</summary>
    public long ProviderTransactionId { get; set; }

    /// <summary>RECEIVED, MATCHED, RECONCILIATION_REQUIRED, RESOLVED.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Lý do cần đối chiếu (ví dụ AMOUNT_MISMATCH); null khi giao dịch chưa lệch.</summary>
    public string? ReconciliationReason { get; set; }

    /// <summary>IN hoặc OUT.</summary>
    public string TransferType { get; set; } = string.Empty;

    public long TransferAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Đơn thanh toán liên quan, nếu đã xác định được.</summary>
    public Guid? PaymentOrderId { get; set; }

    /// <summary>Mã thanh toán LV... tìm được từ giao dịch, nếu có.</summary>
    public string? PaymentCode { get; set; }

    public string? Gateway { get; set; }

    public string? ReferenceCode { get; set; }

    /// <summary>Giờ tiền vào theo SePay (UTC); null nếu không đọc được.</summary>
    public DateTimeOffset? TransactionAt { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }
}
