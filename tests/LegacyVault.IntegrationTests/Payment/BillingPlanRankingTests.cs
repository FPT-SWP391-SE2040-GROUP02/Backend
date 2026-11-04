using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Billing;
using Microsoft.Extensions.Options;
using Xunit;

namespace LegacyVault.IntegrationTests.Payment;

/// <summary>Admin đặt giá gói thế nào cũng được: gói tự xếp từ rẻ đến đắt, không còn bị từ chối vì rẻ hơn gói trước.</summary>
public sealed class BillingPlanRankingTests
{
    private static BillingPriceSettings Build(long goldOneYear)
    {
        BillingSettings settings = new BillingSettings();
        settings.Pricing.PolicyVersion = "TEST";
        settings.Pricing.SlotPricePerYear = 49000;
        settings.Pricing.Xs = new BillingTierPricesSection { OneYear = 199000, ThreeYears = 499000, FiveYears = 799000 };
        settings.Pricing.XsMax = new BillingTierPricesSection { OneYear = 399000, ThreeYears = 999000, FiveYears = 1599000 };
        BillingPlanSection gold = new BillingPlanSection
        {
            Code = "GOLD",
            Name = "Gold",
            Order = 30,
            OneYear = goldOneYear,
            ThreeYears = goldOneYear * 2,
            FiveYears = goldOneYear * 3,
        };
        settings.Pricing.Custom["GOLD"] = gold;
        return new BillingPriceSettings(Options.Create(settings));
    }

    [Fact]
    public void CheaperCustomPlan_IsAccepted_AndPlacedBeforeTheDearerPlans()
    {
        BillingPriceSettings prices = Build(goldOneYear: 1000);

        List<string> codes = prices.Plans.Select(plan => plan.Code).ToList();
        Assert.Equal(new[] { "GOLD", "XS", "XS_MAX" }, codes);
        Assert.Equal(new[] { 10, 20, 30 }, prices.Plans.Select(plan => plan.Order).ToArray());
    }

    [Fact]
    public void DearerCustomPlan_StaysLast()
    {
        BillingPriceSettings prices = Build(goldOneYear: 900000);

        Assert.Equal(new[] { "XS", "XS_MAX", "GOLD" }, prices.Plans.Select(plan => plan.Code).ToArray());
    }

    [Fact]
    public void TierPriceAboveCap_IsRejected()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Build(goldOneYear: BillingPrices.MaxTierPrice + 1));

        Assert.Contains("vượt mức tối đa", error.Message);
    }

    [Fact]
    public void TierPriceAtCap_IsAccepted()
    {
        // 3 năm và 5 năm của gói Gold tính theo bội số nên chỉ thử kỳ 1 năm ở mức trần với giá bội nhỏ hơn
        BillingSettings settings = new BillingSettings();
        settings.Pricing.PolicyVersion = "TEST";
        settings.Pricing.SlotPricePerYear = BillingPrices.MaxSlotPricePerYear;
        settings.Pricing.Xs = new BillingTierPricesSection
        {
            OneYear = BillingPrices.MaxTierPrice,
            ThreeYears = BillingPrices.MaxTierPrice,
            FiveYears = BillingPrices.MaxTierPrice,
        };
        settings.Pricing.XsMax = new BillingTierPricesSection { OneYear = 1, ThreeYears = 1, FiveYears = 1 };

        BillingPriceSettings prices = new BillingPriceSettings(Options.Create(settings));

        Assert.Equal(BillingPrices.MaxTierPrice, prices.FindPlan("XS")!.Prices[1]);
    }

    [Fact]
    public void SlotPriceAboveCap_IsRejected()
    {
        BillingSettings settings = new BillingSettings();
        settings.Pricing.PolicyVersion = "TEST";
        settings.Pricing.SlotPricePerYear = BillingPrices.MaxSlotPricePerYear + 1;
        settings.Pricing.Xs = new BillingTierPricesSection { OneYear = 1, ThreeYears = 1, FiveYears = 1 };
        settings.Pricing.XsMax = new BillingTierPricesSection { OneYear = 1, ThreeYears = 1, FiveYears = 1 };

        Assert.Throws<InvalidOperationException>(() => new BillingPriceSettings(Options.Create(settings)));
    }

    [Fact]
    public void IncludedSlotsAboveTotalCap_IsReportedForSaving_ButDoesNotBreakPricing()
    {
        BillingSettings settings = new BillingSettings();
        settings.Pricing.PolicyVersion = "TEST";
        settings.Pricing.SlotPricePerYear = 49000;
        settings.Pricing.MaxTotalSlots = 3;
        settings.Pricing.Xs = new BillingTierPricesSection { OneYear = 1, ThreeYears = 1, FiveYears = 1 };
        settings.Pricing.XsMax = new BillingTierPricesSection { OneYear = 2, ThreeYears = 2, FiveYears = 2 };

        // XS Max kèm 5 suất, trần chỉ 3: lúc chạy vẫn dựng được bảng giá, lúc Admin lưu thì bị báo
        BillingPriceSettings prices = new BillingPriceSettings(Options.Create(settings));

        Assert.Equal("XS_MAX", prices.FindPlanExceedingTotalSlots()!.Code);
        Assert.Equal(2, prices.Plans.Count);
    }

    [Theory]
    [InlineData("  Gold  PLUS ", "gold plus")]
    [InlineData("GOLD", "gold")]
    public void PlanName_IsNormalizedForDuplicateCheck(string input, string expected)
    {
        Assert.Equal(expected, PlanPriceRules.NormalizeName(input));
    }

    [Fact]
    public void HiddenPlan_KeepsItsRank_WhenCheaperThanTheOthers()
    {
        BillingSettings settings = new BillingSettings();
        settings.Pricing.PolicyVersion = "TEST";
        settings.Pricing.SlotPricePerYear = 49000;
        settings.Pricing.Xs = new BillingTierPricesSection { OneYear = 10000, ThreeYears = 499000, FiveYears = 799000, Hidden = true };
        settings.Pricing.XsMax = new BillingTierPricesSection { OneYear = 50000, ThreeYears = 999000, FiveYears = 1599000 };

        BillingPriceSettings prices = new BillingPriceSettings(Options.Create(settings));

        Assert.Equal(new[] { "XS", "XS_MAX" }, prices.Plans.Select(plan => plan.Code).ToArray());
        Assert.True(prices.Plans[0].Hidden);
    }
}
