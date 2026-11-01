using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Hạn mức và luật của bảng giá (SRS §8.1, §8.2). Các con số tiền (giá hạng, giá suất thêm) và PolicyVersion
/// nằm trong cấu hình "Billing:Pricing", đọc qua <see cref="IBillingPriceSettings"/>. Đơn thanh toán lưu giá đã chốt
/// và PolicyVersion nên đổi bảng giá không ảnh hưởng đơn cũ (PAY-11).
/// </summary>
/// <remarks>Mỗi lần đổi giá hoặc hạn mức phải đổi PolicyVersion.</remarks>
public static class BillingPrices
{
    /// <summary>Tiền tệ duy nhất của hệ thống.</summary>
    public const string Currency = "VND";

    /// <summary>Số suất thêm tối đa trong một đơn (§8.2).</summary>
    public const int MaxExtraSlotsPerOrder = 20;

    /// <summary>Một năm dịch vụ = 365 ngày (PAY-08).</summary>
    public const int DaysPerYear = 365;

    /// <summary>365 ngày tính bằng giây, mẫu số của công thức ADDON-02.</summary>
    public const long SecondsPerYear = 31536000;

    /// <summary>1 MiB = 1.048.576 byte (QUOTA-01).</summary>
    private const long MiB = 1048576;

    /// <summary>Đơn chờ thanh toán trong 15 phút kể từ lúc tạo (§8.3 bước 4, ADDON-03).</summary>
    public static readonly TimeSpan OrderLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Lượt eKYC Owner phải đạt trong vòng 90 ngày mới được mua mới (§8.3).</summary>
    public static readonly TimeSpan OwnerEkycValidity = TimeSpan.FromDays(90);

    /// <summary>Các số năm được bán.</summary>
    public static readonly int[] SupportedYears = new int[] { 1, 3, 5 };

    /// <summary>Các hạng được bán; Free không có đơn (§8.1).</summary>
    public static readonly ServiceTier[] SellableTiers = new ServiceTier[] { ServiceTier.Xs, ServiceTier.XsMax };

    public static bool IsSellableTier(ServiceTier tier)
    {
        return tier == ServiceTier.Xs || tier == ServiceTier.XsMax;
    }

    public static bool IsSupportedYears(int years)
    {
        for (int i = 0; i < SupportedYears.Length; i++)
        {
            if (SupportedYears[i] == years)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Dung lượng theo hạng. Free hoặc hạng lạ → ArgumentOutOfRangeException.</summary>
    public static long GetStorageQuotaBytes(ServiceTier tier)
    {
        if (tier == ServiceTier.Xs)
        {
            return 200 * MiB;
        }
        if (tier == ServiceTier.XsMax)
        {
            return 500 * MiB;
        }
        throw new ArgumentOutOfRangeException(nameof(tier), "Hạng không có trong bảng giá.");
    }

    /// <summary>Số suất người nhận kèm theo hạng, giữ nguyên suốt kỳ.</summary>
    public static int GetIncludedSlots(ServiceTier tier)
    {
        if (tier == ServiceTier.Xs)
        {
            return 2;
        }
        if (tier == ServiceTier.XsMax)
        {
            return 5;
        }
        throw new ArgumentOutOfRangeException(nameof(tier), "Hạng không có trong bảng giá.");
    }
}
