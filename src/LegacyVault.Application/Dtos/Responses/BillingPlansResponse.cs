namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Bảng giá hiện hành (GET /api/v1/billing/plans), SRS §8.1, §8.2.</summary>
public class BillingPlansResponse
{
    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>"VND".</summary>
    public string Currency { get; set; } = string.Empty;

    public List<BillingTierResponse> Tiers { get; set; } = new List<BillingTierResponse>();

    public BillingExtraSlotResponse ExtraSlot { get; set; } = new BillingExtraSlotResponse();

    public string Notice { get; set; } = string.Empty;
}

/// <summary>Một hạng bán được: "XS" hoặc "XS_MAX".</summary>
public class BillingTierResponse
{
    public string Tier { get; set; } = string.Empty;
    public long StorageQuotaBytes { get; set; }
    public int IncludedBeneficiarySlots { get; set; }
    public List<BillingPriceResponse> Prices { get; set; } = new List<BillingPriceResponse>();
}

/// <summary>Giá cho cả kỳ theo số năm.</summary>
public class BillingPriceResponse
{
    public int Years { get; set; }
    public long Amount { get; set; }

    /// <summary>Số năm × 365 (PAY-08).</summary>
    public int ServiceDays { get; set; }
}

/// <summary>Suất người nhận mua thêm.</summary>
public class BillingExtraSlotResponse
{
    /// <summary>Giá một suất cho một năm.</summary>
    public long PricePerYear { get; set; }

    public int MaxPerOrder { get; set; }
}
