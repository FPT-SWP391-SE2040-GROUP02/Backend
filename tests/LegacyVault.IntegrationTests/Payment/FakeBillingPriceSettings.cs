using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>
/// Bảng giá cố định cho test thuần logic (không đọc cấu hình), trùng giá SRS §8.1 phiên bản 2026-10
/// nên các test vẫn kiểm đúng số tiền hợp đồng.
/// </summary>
public sealed class FakeBillingPriceSettings : IBillingPriceSettings
{
    public string PolicyVersion
    {
        get { return "2026-10"; }
    }

    public long SlotPricePerYear
    {
        get { return 49000; }
    }

    public int MaxExtraSlotsPerOrder
    {
        get { return BillingPrices.MaxExtraSlotsPerOrder; }
    }

    public TimeSpan OrderLifetime
    {
        get { return BillingPrices.OrderLifetime; }
    }

    public IReadOnlyList<string> FreeFeatures
    {
        get { return new List<string>(); }
    }

    public IReadOnlyList<PlanInfo> Plans
    {
        get
        {
            return new PlanInfo[] { BuildPlan(ServiceTier.Xs, "XS", "Legacy XS", 10), BuildPlan(ServiceTier.XsMax, "XS_MAX", "Legacy XS Max", 20) };
        }
    }

    public PlanInfo? FindPlan(string code)
    {
        foreach (PlanInfo plan in Plans)
        {
            if (plan.Code == code.Trim().ToUpperInvariant())
            {
                return plan;
            }
        }
        return null;
    }

    private PlanInfo BuildPlan(ServiceTier tier, string code, string name, int order)
    {
        PlanInfo plan = new PlanInfo();
        plan.Code = code;
        plan.Tier = tier;
        plan.Name = name;
        plan.Order = order;
        plan.StorageQuotaBytes = GetStorageQuotaBytes(tier);
        plan.IncludedSlots = GetIncludedSlots(tier);
        Dictionary<int, long> prices = new Dictionary<int, long>();
        foreach (int years in SaleYears)
        {
            prices[years] = GetTierPrice(tier, years);
        }
        plan.Prices = prices;
        return plan;
    }

    public long GetStorageQuotaBytes(ServiceTier tier)
    {
        return BillingPrices.GetStorageQuotaBytes(tier);
    }

    public int GetIncludedSlots(ServiceTier tier)
    {
        return BillingPrices.GetIncludedSlots(tier);
    }

    public IReadOnlyList<int> SaleYears
    {
        get { return new int[] { 1, 3, 5 }; }
    }

    public bool HasTierPrice(ServiceTier tier, int years)
    {
        return years == 1 || years == 3 || years == 5;
    }

    public long GetTierPrice(ServiceTier tier, int years)
    {
        if (tier == ServiceTier.Xs)
        {
            if (years == 1)
            {
                return 199000;
            }
            if (years == 3)
            {
                return 499000;
            }
            if (years == 5)
            {
                return 799000;
            }
        }
        if (tier == ServiceTier.XsMax)
        {
            if (years == 1)
            {
                return 399000;
            }
            if (years == 3)
            {
                return 999000;
            }
            if (years == 5)
            {
                return 1599000;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(tier), "Hạng hoặc số năm không có trong bảng giá.");
    }
}
