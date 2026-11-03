namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Mục cấu hình "Billing". Bank chỉ chứa thông tin công khai của tài khoản nhận tiền.
/// Tài khoản thật và SePay:WebhookApiKey (secret) đặt trong user-secrets hoặc biến môi trường, không ghi vào appsettings của repo.
/// </summary>
public class BillingSettings
{
    public const string SectionName = "Billing";

    public BillingBankSection Bank { get; set; } = new BillingBankSection();

    public BillingSePaySection SePay { get; set; } = new BillingSePaySection();

    /// <summary>Bảng giá bán (SRS §8.1, ADDON-01). Không phải secret nên đặt trong appsettings.json.</summary>
    public BillingPricingSection Pricing { get; set; } = new BillingPricingSection();

    /// <summary>
    /// Công tắc endpoint giả lập trả tiền (dev-pay). Mặc định tắt; chỉ có tác dụng khi môi trường là Development
    /// (xem BillingDevSettings). Không đặt true trong appsettings.json hay môi trường thật.
    /// </summary>
    public bool DevPaymentEnabled { get; set; }
}

public class BillingBankSection
{
    /// <summary>Mã ngân hàng theo SePay/VietQR, ví dụ "MBBank".</summary>
    public string BankCode { get; set; } = string.Empty;

    public string AccountNumber { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;
}

public class BillingSePaySection
{
    /// <summary>Secret: key SePay gửi trong header Authorization: Apikey. Chỉ đặt bằng user-secrets / biến môi trường.</summary>
    public string WebhookApiKey { get; set; } = string.Empty;

    /// <summary>"Test" hoặc "Live" (PAY-01). Mặc định Test.</summary>
    public string Environment { get; set; } = "Test";
}

public class BillingPricingSection
{
    /// <summary>Phiên bản chính sách giá, ghi vào đơn lúc tạo (PAY-11). Đổi giá thì đổi giá trị này.</summary>
    public string PolicyVersion { get; set; } = string.Empty;

    /// <summary>Giá một suất người nhận thêm cho một năm, đơn vị đồng (ADDON-01).</summary>
    public long SlotPricePerYear { get; set; }

    public BillingTierPricesSection Xs { get; set; } = new BillingTierPricesSection();

    public BillingTierPricesSection XsMax { get; set; } = new BillingTierPricesSection();

    /// <summary>
    /// Các kỳ (số năm) đang bán do Admin đặt. null = chưa đặt, bán các kỳ 1, 3 và 5 năm như cấu hình gốc.
    /// </summary>
    public List<int>? SaleYears { get; set; }

    /// <summary>Gói Admin tạo thêm, khóa là mã gói (ví dụ "GOLD"). Cấu hình gốc không có; chỉ do cài đặt hệ thống thêm vào.</summary>
    public Dictionary<string, BillingPlanSection> Custom { get; set; } = new Dictionary<string, BillingPlanSection>();
}

/// <summary>Một gói Admin tạo thêm: thông tin hiển thị, thứ tự và giá, hạn mức như gói gốc.</summary>
public class BillingPlanSection : BillingTierPricesSection
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Thứ tự gói: gói gốc XS = 10, XS_MAX = 20; gói thêm đặt số lớn hơn để đứng sau.</summary>
    public int Order { get; set; }
}

/// <summary>Giá một hạng cho từng kỳ, đơn vị đồng: 1, 3 và 5 năm trong cấu hình gốc, kỳ khác do Admin thêm.</summary>
public class BillingTierPricesSection
{
    public long OneYear { get; set; }

    public long ThreeYears { get; set; }

    public long FiveYears { get; set; }

    /// <summary>false khi Admin tạm ngừng mở bán gói này.</summary>
    public bool OnSale { get; set; } = true;

    /// <summary>true khi Admin đã xóa (ẩn) gói: không bán nữa, nên không bắt buộc có giá cho mọi kỳ.</summary>
    public bool Hidden { get; set; }

    /// <summary>Dung lượng của hạng (MiB) do Admin đặt; null = dùng mức mặc định trong BillingPrices.</summary>
    public long? QuotaMb { get; set; }

    /// <summary>Số suất người nhận kèm theo hạng do Admin đặt; null = dùng mức mặc định trong BillingPrices.</summary>
    public int? IncludedSlots { get; set; }

    /// <summary>Giá các kỳ ngoài 1, 3, 5 năm (Admin thêm kỳ bán mới). Khóa là số năm.</summary>
    public Dictionary<int, long> Extra { get; set; } = new Dictionary<int, long>();

    /// <summary>Giá của kỳ này; 0 nếu chưa có.</summary>
    public long Get(int years)
    {
        if (years == 1)
        {
            return OneYear;
        }
        if (years == 3)
        {
            return ThreeYears;
        }
        if (years == 5)
        {
            return FiveYears;
        }
        long value;
        if (Extra.TryGetValue(years, out value))
        {
            return value;
        }
        return 0;
    }

    public void Set(int years, long value)
    {
        if (years == 1)
        {
            OneYear = value;
        }
        else if (years == 3)
        {
            ThreeYears = value;
        }
        else if (years == 5)
        {
            FiveYears = value;
        }
        else
        {
            Extra[years] = value;
        }
    }
}
