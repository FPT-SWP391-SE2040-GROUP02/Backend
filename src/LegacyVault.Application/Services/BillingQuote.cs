using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Kết quả tính giá của <see cref="BillingRules"/>. Báo giá và tạo đơn cùng dùng một kết quả này (D2),
/// nên số tiền trên đơn luôn khớp số tiền đã báo.
/// </summary>
public class BillingQuote
{
    public PaymentOrderKind Kind { get; set; }

    /// <summary>Hạng được mua; với ADDON là hạng hiện tại của kho.</summary>
    public ServiceTier Tier { get; set; }

    /// <summary>Số năm; null với ADDON.</summary>
    public int? Years { get; set; }

    public int ExtraSlots { get; set; }

    /// <summary>Tiền hạng; 0 với ADDON.</summary>
    public long TierAmount { get; set; }

    public long SlotAmount { get; set; }

    public long TotalAmount { get; set; }

    public long StorageQuotaBytes { get; set; }

    public int IncludedBeneficiarySlots { get; set; }

    /// <summary>Tổng suất sau khi thanh toán xong: kèm theo hạng + suất thêm hiện có + suất mua lần này.</summary>
    public int TotalBeneficiarySlots { get; set; }

    /// <summary>Số ngày của kỳ mới (số năm × 365); null với ADDON vì không đổi hạn.</summary>
    public int? ServiceDays { get; set; }

    /// <summary>ADDON: hạn kỳ hiện tại tại lúc báo giá; PURCHASE: null.</summary>
    public DateTimeOffset? BaseServiceExpiresAt { get; set; }
}
