namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Báo giá (POST /api/v1/billing/quote), SRS §8.3. Không ghi DB, chưa tạo đơn.</summary>
public class BillingQuoteResponse
{
    /// <summary>"PURCHASE", "RENEWAL" hoặc "UPGRADE".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Mã gói được báo giá.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Null với nâng hạng.</summary>
    public int? Years { get; set; }

    public long TierAmount { get; set; }
    public long TotalAmount { get; set; }

    /// <summary>"VND".</summary>
    public string Currency { get; set; } = string.Empty;

    public long StorageQuotaBytes { get; set; }
    public int IncludedBeneficiarySlots { get; set; }

    /// <summary>Số ngày của kỳ mới; null với nâng hạng.</summary>
    public int? ServiceDays { get; set; }

    /// <summary>"ON_PAYMENT" với mua mới (kỳ bắt đầu lúc thanh toán thành công); null với nâng hạng.</summary>
    public string? ServiceStartsAt { get; set; }

    /// <summary>Nâng hạng và gia hạn sớm: hạn kỳ hiện tại; mua mới: null.</summary>
    public DateTimeOffset? ServiceExpiresAt { get; set; }

    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>"Thanh toán không tự kích hoạt bàn giao." (§8.3 bước 3).</summary>
    public string Notice { get; set; } = string.Empty;

    public DateTimeOffset ServerTime { get; set; }
}
