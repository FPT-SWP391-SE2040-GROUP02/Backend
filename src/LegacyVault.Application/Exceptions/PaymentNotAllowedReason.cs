namespace LegacyVault.Application.Exceptions;

/// <summary>Giá trị trường <c>reason</c> đi kèm PAYMENT_NOT_ALLOWED (trả dạng UPPER_SNAKE_CASE).</summary>
public enum PaymentNotAllowedReason
{
    /// <summary>Kho đã đóng, không mua hay gia hạn (SETUP-18, AC-104).</summary>
    VaultClosed,
    /// <summary>Kế hoạch đang chứng tử, bàn giao hoặc bị chặn: không tạo đơn mới (§8.3).</summary>
    PlanLocked,
    /// <summary>Mua mới cần eKYC Owner đạt trong 90 ngày (§8.3).</summary>
    OwnerEkycRequired,
    /// <summary>Gia hạn, kỳ chờ, ân hạn chưa hỗ trợ ở phiên bản này.</summary>
    RenewalNotSupported,

    /// <summary>Gia hạn sang gói nhỏ hơn mức đang dùng (dung lượng hoặc suất người nhận).</summary>
    UsageExceedsPlan,

    /// <summary>Kho đang có dịch vụ còn hạn mà không gia hạn cũng không nâng lên gói cao hơn: không có gì để mua.</summary>
    NothingToBuy,

    /// <summary>Admin đã tạm ngừng mở bán hạng này (Plans.Xs.On / Plans.XsMax.On = false): không tạo đơn mới của hạng đó (mua mới, nâng hạng, gia hạn), kể cả người đang dùng.</summary>
    TierNotOnSale,
}
