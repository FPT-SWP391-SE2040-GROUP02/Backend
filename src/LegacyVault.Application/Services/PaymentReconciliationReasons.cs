namespace LegacyVault.Application.Services;

/// <summary>
/// Lý do một giao dịch tiền vào không tự cấp được dịch vụ, lưu ở PaymentTransactions.ReconciliationReason (tối đa 32 ký tự).
/// Ngoài các hằng này, khi PaymentGranter từ chối thì lý do là trường reason của PAYMENT_ORDER_NOT_PAYABLE
/// (ENTITLEMENT_CHANGED, VAULT_CLOSED, EXPIRED, CANCELLED, PAID). Thứ tự kiểm tra: SePayRules.FindReconciliationReason.
/// </summary>
public static class PaymentReconciliationReasons
{
    /// <summary>Giao dịch tiền ra, không phải tiền vào. Không mở sự cố.</summary>
    public const string NotIncoming = "NOT_INCOMING";

    /// <summary>Tiền vào tài khoản khác tài khoản nhận cấu hình (PAY-03).</summary>
    public const string WrongAccount = "WRONG_ACCOUNT";

    /// <summary>Không tìm thấy mã thanh toán trong code hay nội dung chuyển khoản.</summary>
    public const string CodeMissing = "CODE_MISSING";

    /// <summary>Có mã nhưng không có đơn nào mang mã đó.</summary>
    public const string OrderNotFound = "ORDER_NOT_FOUND";

    /// <summary>Số tiền khác tổng tiền của đơn, thiếu hay thừa đều lệch; không cộng dồn (AC-28).</summary>
    public const string AmountMismatch = "AMOUNT_MISMATCH";

    /// <summary>Đơn đã được thanh toán bởi giao dịch khác (PAY-04, AC-97).</summary>
    public const string AlreadyPaid = "ALREADY_PAID";

    /// <summary>Đơn đã bị Owner hủy (PAY-07).</summary>
    public const string OrderCancelled = "ORDER_CANCELLED";

    /// <summary>Không đọc được giờ tiền vào nên không so được với hạn đơn.</summary>
    public const string TimeUnknown = "TIME_UNKNOWN";

    /// <summary>Tiền vào từ hạn đơn trở đi (PAY-06, AC-30).</summary>
    public const string LateAfterExpiry = "LATE_AFTER_EXPIRY";
}
