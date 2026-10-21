namespace LegacyVault.Application.Exceptions;

/// <summary>Giá trị trường <c>reason</c> đi kèm PAYMENT_ORDER_NOT_PAYABLE (trả dạng UPPER_SNAKE_CASE).</summary>
public enum PaymentOrderNotPayableReason
{
    /// <summary>Đơn đã thanh toán, không hủy hay cấp quyền lần nữa (PAY-04).</summary>
    Paid,
    /// <summary>Đơn đã quá hạn thanh toán.</summary>
    Expired,
    /// <summary>Đơn đã bị hủy (PAY-07).</summary>
    Cancelled,
    /// <summary>Kho đã đóng, không cấp quyền dịch vụ (AC-104).</summary>
    VaultClosed,
    /// <summary>Quyền dịch vụ của kho đã đổi so với lúc tạo đơn (ADDON-03, D8): không tự cấp.</summary>
    EntitlementChanged,
}
