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

    /// <summary>Mô tả tính năng của gói Free do Admin viết; rỗng thì Client dùng mô tả mặc định.</summary>
    public List<string> FreeFeatures { get; set; } = new List<string>();

    /// <summary>Dung lượng và số suất người nhận của gói Free theo cấu hình Admin hiện hành.</summary>
    public FreePlanResponse Free { get; set; } = new FreePlanResponse();
}

/// <summary>Hạn mức gói Free (không bán nên không có giá hay kỳ).</summary>
public class FreePlanResponse
{
    public long StorageQuotaBytes { get; set; }

    public int IncludedBeneficiarySlots { get; set; }
}

/// <summary>Một hạng bán được: "XS" hoặc "XS_MAX".</summary>
public class BillingTierResponse
{
    /// <summary>Mã gói: "XS", "XS_MAX" hoặc mã gói Admin tạo thêm.</summary>
    public string Tier { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Thứ tự gói tăng dần; gói đứng sau là gói cao hơn khi nâng hạng.</summary>
    public int Order { get; set; }

    /// <summary>false khi Admin đã tạm ngừng mở bán hoặc xóa gói: Client ẩn nút mua; Backend vẫn chặn nếu gọi thẳng.</summary>
    public bool OnSale { get; set; } = true;

    /// <summary>true khi Admin đã xóa gói: Client không vẽ thẻ gói nhưng vẫn biết gói này để so thứ tự.</summary>
    public bool Hidden { get; set; }

    /// <summary>Các dòng mô tả tính năng do Admin viết; rỗng thì Client dùng mô tả mặc định.</summary>
    public List<string> Features { get; set; } = new List<string>();

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
