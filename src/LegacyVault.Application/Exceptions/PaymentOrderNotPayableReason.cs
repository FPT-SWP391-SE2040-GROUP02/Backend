namespace LegacyVault.Application.Exceptions;

/// <summary>Giá trị trường <c>reason</c> đi kèm PAYMENT_ORDER_NOT_PAYABLE (trả dạng UPPER_SNAKE_CASE).</summary>
public enum PaymentOrderNotPayableReason
{
    /// <summary>Đơn đã thanh toán, không hủy hay cấp quyền lần nữa (PAY-04).</summary>
    Paid,
    /// <summary>Đơn đã quá hạn thanh toán.</summary>
    Expired,
}
