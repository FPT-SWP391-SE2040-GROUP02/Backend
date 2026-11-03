using LegacyVault.Domain.Entities;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Quy ước nối các tham số "Plans.*" trong bảng SystemSettings với bảng giá "Billing:Pricing".
/// Chỉ dòng do Admin đã sửa (UpdatedBy khác null) và đang bật mới đè lên cấu hình; dòng mặc định tạo sẵn thì không,
/// để giá đặt trong appsettings hoặc user-secrets vẫn có tác dụng khi Admin chưa chỉnh.
/// </summary>
public static class PlanPriceRules
{
    public const string PlanKeyPrefix = "Plans.";

    /// <summary>Tiền tố PolicyVersion khi giá đang bị Admin đè. Cột PolicyVersion của đơn dài tối đa 16 ký tự.</summary>
    private const string PolicyVersionPrefix = "DB-";

    public static bool IsPriceKey(string key)
    {
        BillingPricingSection probe = new BillingPricingSection();
        return TryApply(probe, key, 1);
    }

    /// <summary>Gán giá trị vào đúng ô của bảng giá theo tên tham số. Trả false nếu tên không phải tham số giá.</summary>
    public static bool TryApply(BillingPricingSection pricing, string key, long value)
    {
        if (key == "Plans.Xs.Price1Y")
        {
            pricing.Xs.OneYear = value;
            return true;
        }
        if (key == "Plans.Xs.Price3Y")
        {
            pricing.Xs.ThreeYears = value;
            return true;
        }
        if (key == "Plans.Xs.Price5Y")
        {
            pricing.Xs.FiveYears = value;
            return true;
        }
        if (key == "Plans.XsMax.Price1Y")
        {
            pricing.XsMax.OneYear = value;
            return true;
        }
        if (key == "Plans.XsMax.Price3Y")
        {
            pricing.XsMax.ThreeYears = value;
            return true;
        }
        if (key == "Plans.XsMax.Price5Y")
        {
            pricing.XsMax.FiveYears = value;
            return true;
        }
        if (key == "Plans.Addon.SlotPricePerYear")
        {
            pricing.SlotPricePerYear = value;
            return true;
        }
        return false;
    }

    public static BillingPricingSection Clone(BillingPricingSection source)
    {
        BillingPricingSection copy = new BillingPricingSection();
        copy.PolicyVersion = source.PolicyVersion;
        copy.SlotPricePerYear = source.SlotPricePerYear;
        copy.Xs.OneYear = source.Xs.OneYear;
        copy.Xs.ThreeYears = source.Xs.ThreeYears;
        copy.Xs.FiveYears = source.Xs.FiveYears;
        copy.XsMax.OneYear = source.XsMax.OneYear;
        copy.XsMax.ThreeYears = source.XsMax.ThreeYears;
        copy.XsMax.FiveYears = source.XsMax.FiveYears;
        return copy;
    }

    /// <summary>
    /// Bảng giá đang hiệu lực = cấu hình gốc + các dòng giá Admin đã sửa. hasOverride=false nghĩa là không có dòng nào đè
    /// (PolicyVersion giữ nguyên của cấu hình gốc); ngược lại PolicyVersion đổi theo lần sửa gần nhất (PAY-11).
    /// </summary>
    public static BillingPricingSection BuildEffective(
        BillingPricingSection basePricing, List<SystemSetting> rows, out bool hasOverride)
    {
        BillingPricingSection effective = Clone(basePricing);
        hasOverride = false;
        DateTimeOffset latestUpdate = DateTimeOffset.MinValue;

        foreach (SystemSetting row in rows)
        {
            if (row.UpdatedBy == null || !row.IsEnabled)
            {
                continue;
            }

            long value;
            if (!long.TryParse(row.Value, out value))
            {
                continue;
            }
            if (!TryApply(effective, row.Key, value))
            {
                continue;
            }

            hasOverride = true;
            if (row.UpdatedAt > latestUpdate)
            {
                latestUpdate = row.UpdatedAt;
            }
        }

        if (hasOverride)
        {
            effective.PolicyVersion = PolicyVersionPrefix + latestUpdate.UtcDateTime.ToString("yyMMddHHmmss");
        }
        return effective;
    }
}
