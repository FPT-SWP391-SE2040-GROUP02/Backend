using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Đọc bảng giá từ cấu hình "Billing:Pricing". Kiểm tra giá trị ngay khi tạo: cấu hình sai là lỗi máy chủ,
/// không được báo giá hay tạo đơn với số tiền sai.
/// </summary>
public sealed class BillingPriceSettings : IBillingPriceSettings
{
    /// <summary>Cột PolicyVersion của đơn dài tối đa 16 ký tự.</summary>
    private const int MaxPolicyVersionLength = 16;

    private readonly BillingPricingSection pricing;

    public BillingPriceSettings(IOptions<BillingSettings> options)
    {
        this.pricing = options.Value.Pricing;
        Validate();
    }

    public string PolicyVersion
    {
        get { return pricing.PolicyVersion; }
    }

    public long SlotPricePerYear
    {
        get { return pricing.SlotPricePerYear; }
    }

    public long GetTierPrice(ServiceTier tier, int years)
    {
        BillingTierPricesSection section;
        if (tier == ServiceTier.Xs)
        {
            section = pricing.Xs;
        }
        else if (tier == ServiceTier.XsMax)
        {
            section = pricing.XsMax;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(tier), "Hạng không có trong bảng giá.");
        }

        if (years == 1)
        {
            return section.OneYear;
        }
        if (years == 3)
        {
            return section.ThreeYears;
        }
        if (years == 5)
        {
            return section.FiveYears;
        }
        throw new ArgumentOutOfRangeException(nameof(years), "Số năm không có trong bảng giá.");
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(pricing.PolicyVersion) || pricing.PolicyVersion.Length > MaxPolicyVersionLength)
        {
            throw new InvalidOperationException(
                "Billing:Pricing:PolicyVersion là bắt buộc và tối đa " + MaxPolicyVersionLength + " ký tự.");
        }
        if (pricing.SlotPricePerYear <= 0)
        {
            throw new InvalidOperationException("Billing:Pricing:SlotPricePerYear phải lớn hơn 0.");
        }

        EnsureTierPrices("Xs", pricing.Xs);
        EnsureTierPrices("XsMax", pricing.XsMax);

        // Hạng cao hơn không được rẻ hơn hạng thấp cùng kỳ, nếu không giá nâng hạng (XS_MAX trừ XS) sẽ âm.
        if (pricing.XsMax.OneYear < pricing.Xs.OneYear
            || pricing.XsMax.ThreeYears < pricing.Xs.ThreeYears
            || pricing.XsMax.FiveYears < pricing.Xs.FiveYears)
        {
            throw new InvalidOperationException("Billing:Pricing: giá XsMax không được thấp hơn giá Xs cùng kỳ.");
        }
    }

    private static void EnsureTierPrices(string name, BillingTierPricesSection section)
    {
        if (section.OneYear <= 0 || section.ThreeYears <= 0 || section.FiveYears <= 0)
        {
            throw new InvalidOperationException(
                "Billing:Pricing:" + name + " phải có giá OneYear, ThreeYears, FiveYears lớn hơn 0.");
        }
    }
}
