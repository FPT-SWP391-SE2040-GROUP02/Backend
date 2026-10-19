using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Giao dịch tiền vào do nhà cung cấp thanh toán (SePay) gửi qua webhook (bảng PaymentTransactions), SRS §8.3, §8.5, §11.6.
/// </summary>
/// <remarks>
/// - Lưu giao dịch trước, đối chiếu với đơn sau (PAY-05).
/// - Unique (Provider, ProviderTransactionId): webhook gửi lặp không tạo thêm dòng (AC-29).
/// - Một đơn chỉ có tối đa một giao dịch Matched (PAY-04): unique có lọc trên PaymentOrderId khi Status = Matched.
/// - PaymentOrderId được đặt khi tìm được đơn, kể cả khi giao dịch bị lệch, để Admin thấy giao dịch thuộc đơn nào.
/// - Số tiền tính bằng VND (đồng, số nguyên).
/// - Không ghi log Content, AccountNumber.
/// </remarks>
public class PaymentTransaction
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Nhà cung cấp, hiện chỉ có "SePay".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Môi trường cấu hình: "Test" hoặc "Live" (PAY-01).</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ID giao dịch phía SePay (trường id trong webhook).</summary>
    public long ProviderTransactionId { get; set; }

    public string? Gateway { get; set; }

    /// <summary>Số tài khoản nhận tiền.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>Trường code do SePay nhận diện được.</summary>
    public string? Code { get; set; }

    /// <summary>Mã thanh toán LV... tìm được từ code hoặc nội dung chuyển khoản.</summary>
    public string? MatchedPaymentCode { get; set; }

    /// <summary>Nội dung chuyển khoản, đã cắt nếu quá dài.</summary>
    public string? Content { get; set; }

    /// <summary>"in" (tiền vào) hoặc "out" (tiền ra).</summary>
    public string TransferType { get; set; } = string.Empty;

    public long TransferAmount { get; set; }

    public string? ReferenceCode { get; set; }

    /// <summary>Giờ tiền vào (giờ VN đã đổi sang UTC); null nếu không đọc được.</summary>
    public DateTimeOffset? TransactionAt { get; set; }

    /// <summary>Giờ backend nhận webhook.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public PaymentTransactionStatus Status { get; set; }

    /// <summary>Lý do lệch khi Status = ReconciliationRequired, ví dụ AMOUNT_MISMATCH.</summary>
    public string? ReconciliationReason { get; set; }

    public Guid? PaymentOrderId { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
