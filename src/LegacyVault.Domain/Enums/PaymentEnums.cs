namespace LegacyVault.Domain.Enums;

/// <summary>Loại đơn thanh toán (SRS §8.2).</summary>
public enum PaymentOrderKind
{
    /// <summary>Mua mới một hạng dịch vụ cho kho Free (kèm số suất thêm tùy chọn).</summary>
    Purchase = 0,
    /// <summary>Mua thêm suất người nhận giữa kỳ (ADDON-01..03).</summary>
    Addon = 1,
}

/// <summary>Trạng thái đơn thanh toán.</summary>
public enum PaymentOrderStatus
{
    /// <summary>Đang chờ thanh toán; mỗi kho chỉ có một đơn Pending (PAY-04).</summary>
    Pending = 0,
    /// <summary>Đã thanh toán và đã cấp quyền dịch vụ.</summary>
    Paid = 1,
    /// <summary>Quá hạn thanh toán mà chưa trả.</summary>
    Expired = 2,
    /// <summary>Owner hủy đơn.</summary>
    Cancelled = 3,
}
