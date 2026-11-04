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
}
