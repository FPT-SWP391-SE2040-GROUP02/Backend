using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Kết quả tính giá của <see cref="BillingRules"/>. Báo giá và tạo đơn cùng dùng một kết quả này (D2),
/// nên số tiền trên đơn luôn khớp số tiền đã báo.
/// </summary>
public class BillingQuote
{
    public PaymentOrderKind Kind { get; set; }

    /// <summary>Hạng được mua.</summary>
    public ServiceTier Tier { get; set; }

    /// <summary>Mã gói được mua (XS, XS_MAX hoặc mã gói Admin tạo).</summary>
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>Số năm; null với nâng hạng.</summary>
    public int? Years { get; set; }

    public long TierAmount { get; set; }

    public long TotalAmount { get; set; }

    public long StorageQuotaBytes { get; set; }

    public int IncludedBeneficiarySlots { get; set; }

    /// <summary>Số ngày của kỳ mới (số năm × 365); null với nâng hạng vì không đổi hạn.</summary>
    public int? ServiceDays { get; set; }

    /// <summary>Nâng hạng và gia hạn sớm: hạn kỳ hiện tại tại lúc báo giá; PURCHASE: null.</summary>
    public DateTimeOffset? BaseServiceExpiresAt { get; set; }

    /// <summary>Phiên bản bảng giá đã dùng để tính; ghi vào đơn lúc tạo (PAY-11).</summary>
    public string PolicyVersion { get; set; } = string.Empty;
}
