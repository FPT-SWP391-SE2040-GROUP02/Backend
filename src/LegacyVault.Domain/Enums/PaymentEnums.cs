namespace LegacyVault.Domain.Enums;

/// <summary>Loại đơn thanh toán (SRS §8.2).</summary>
public enum PaymentOrderKind
{
    /// <summary>Mua mới một hạng dịch vụ cho kho Free (kèm số suất thêm tùy chọn).</summary>
    Purchase = 0,
    /// <summary>Mua thêm suất người nhận giữa kỳ (ADDON-01..03).</summary>
    Addon = 1,

    /// <summary>Nâng hạng giữa kỳ (XS lên XS Max): trả phần chênh theo thời gian còn lại, giữ nguyên hạn.</summary>
    Upgrade = 2,
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

/// <summary>Trạng thái xử lý giao dịch tiền vào (SRS §8.5, PAY-05).</summary>
public enum PaymentTransactionStatus
{
    /// <summary>Đã lưu, chưa đối chiếu với đơn.</summary>
    Received = 0,
    /// <summary>Khớp đơn và đã cấp quyền dịch vụ.</summary>
    Matched = 1,
    /// <summary>Không khớp, cần Admin đối chiếu; không tự cấp quyền (PAY-07).</summary>
    ReconciliationRequired = 2,
    /// <summary>Admin đã xử lý (chưa dùng ở H2).</summary>
    Resolved = 3,
}
