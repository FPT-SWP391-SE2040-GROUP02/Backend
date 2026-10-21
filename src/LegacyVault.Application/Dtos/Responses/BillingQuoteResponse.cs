namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Báo giá (POST /api/v1/billing/quote), SRS §8.3. Không ghi DB, chưa tạo đơn.</summary>
public class BillingQuoteResponse
{
    /// <summary>"PURCHASE" hoặc "ADDON".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>"XS" hoặc "XS_MAX"; ADDON là hạng hiện tại của kho.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Null với ADDON.</summary>
    public int? Years { get; set; }

    public int ExtraSlots { get; set; }
    public long TierAmount { get; set; }
    public long SlotAmount { get; set; }
    public long TotalAmount { get; set; }

    /// <summary>"VND".</summary>
    public string Currency { get; set; } = string.Empty;

    public long StorageQuotaBytes { get; set; }
    public int IncludedBeneficiarySlots { get; set; }

    /// <summary>Tổng suất sau khi thanh toán: kèm theo hạng + suất thêm hiện có + suất mua lần này.</summary>
    public int BeneficiarySlots { get; set; }

    /// <summary>Số ngày của kỳ mới; null với ADDON.</summary>
    public int? ServiceDays { get; set; }

    /// <summary>"ON_PAYMENT" với mua mới (kỳ bắt đầu lúc thanh toán thành công); null với ADDON.</summary>
    public string? ServiceStartsAt { get; set; }

    /// <summary>ADDON: hạn kỳ hiện tại (không đổi); mua mới: null.</summary>
    public DateTimeOffset? ServiceExpiresAt { get; set; }

    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>"Thanh toán không tự kích hoạt bàn giao." (§8.3 bước 3).</summary>
    public string Notice { get; set; } = string.Empty;

    public DateTimeOffset ServerTime { get; set; }
}
