using LegacyVault.Application.Services;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Đọc bảng giá từ cấu hình "Billing:Pricing" (đã ghép với giá, kỳ bán và gói Admin chỉnh). Kiểm tra giá trị ngay khi tạo:
/// cấu hình sai là lỗi máy chủ, không được báo giá hay tạo đơn với số tiền sai.
/// </summary>
public sealed class BillingPriceSettings : IBillingPriceSettings
{
    /// <summary>Cột PolicyVersion của đơn dài tối đa 16 ký tự.</summary>
    private const int MaxPolicyVersionLength = 16;

    /// <summary>Số năm một kỳ bán tối đa; chặn giá trị vô lý khi Admin thêm kỳ.</summary>
    public const int MaxSaleYears = 10;

    private const long BytesPerMiB = 1048576;

    /// <summary>Dung lượng và suất mặc định của gói Admin tạo thêm khi chưa đặt.</summary>
    private const long CustomDefaultQuotaMb = 100;

    private const int CustomDefaultSlots = 1;

    private static readonly int[] DefaultSaleYears = new int[] { 1, 3, 5 };

    private readonly BillingPricingSection pricing;
    private readonly List<int> saleYears;
    private readonly List<PlanInfo> plans;

    public BillingPriceSettings(IOptions<BillingSettings> options)
    {
        this.pricing = options.Value.Pricing;
        this.saleYears = ResolveSaleYears(pricing);
        this.plans = BuildPlans();
        Validate();
    }

    /// <summary>Dựng từ một bảng giá đã ghép (cấu hình gốc + giá Admin chỉnh). Giá sai vẫn ném InvalidOperationException.</summary>
    internal BillingPriceSettings(BillingPricingSection pricing)
    {
        this.pricing = pricing;
        this.saleYears = ResolveSaleYears(pricing);
        this.plans = BuildPlans();
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

    public IReadOnlyList<PlanInfo> Plans
    {
        get { return plans; }
    }

    public IReadOnlyList<int> SaleYears
    {
        get { return saleYears; }
    }

    public PlanInfo? FindPlan(string code)
    {
        string wanted = code.Trim().ToUpperInvariant();
        foreach (PlanInfo plan in plans)
        {
            if (plan.Code == wanted)
            {
                return plan;
            }
        }
        return null;
    }

    public long GetStorageQuotaBytes(ServiceTier tier)
    {
        long? mb = SectionOf(tier).QuotaMb;
        if (mb == null)
        {
            return BillingPrices.GetStorageQuotaBytes(tier);
        }
        return mb.Value * BytesPerMiB;
    }

    public int GetIncludedSlots(ServiceTier tier)
    {
        int? slots = SectionOf(tier).IncludedSlots;
        if (slots == null)
        {
            return BillingPrices.GetIncludedSlots(tier);
        }
        return slots.Value;
    }

    public bool HasTierPrice(ServiceTier tier, int years)
    {
        if (!saleYears.Contains(years))
        {
            return false;
        }
        return SectionOf(tier).Get(years) > 0;
    }

    public long GetTierPrice(ServiceTier tier, int years)
    {
        BillingTierPricesSection section = SectionOf(tier);
        long price = section.Get(years);
        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(years), "Số năm không có trong bảng giá.");
        }
        return price;
    }

    private BillingTierPricesSection SectionOf(ServiceTier tier)
    {
        if (tier == ServiceTier.Xs)
        {
            return pricing.Xs;
        }
        if (tier == ServiceTier.XsMax)
        {
            return pricing.XsMax;
        }
        throw new ArgumentOutOfRangeException(nameof(tier), "Hạng không có trong bảng giá.");
    }

    private List<PlanInfo> BuildPlans()
    {
        List<PlanInfo> result = new List<PlanInfo>();
        result.Add(BuildPlan(PlanCodes.Xs, ServiceTier.Xs, "Legacy XS", "Căn bản", PlanPriceRules.XsOrder, pricing.Xs));
        result.Add(BuildPlan(PlanCodes.XsMax, ServiceTier.XsMax, "Legacy XS Max", "Cao cấp", PlanPriceRules.XsMaxOrder, pricing.XsMax));
        foreach (BillingPlanSection custom in pricing.Custom.Values)
        {
            // Chỉ gói đã khai báo trong danh sách gói (có tên) mới tồn tại; khóa lẻ không có tên bị bỏ qua.
            if (custom.Name.Length == 0)
            {
                continue;
            }
            result.Add(BuildPlan(custom.Code, ServiceTier.Custom, custom.Name, custom.Subtitle, custom.Order, custom));
        }
        result.Sort((a, b) => a.Order.CompareTo(b.Order));
        return result;
    }

    private PlanInfo BuildPlan(
        string code, ServiceTier tier, string name, string subtitle, int order, BillingTierPricesSection section)
    {
        PlanInfo plan = new PlanInfo();
        plan.Code = code;
        plan.Tier = tier;
        plan.Name = name;
        plan.Subtitle = subtitle;
        plan.Order = order;
        plan.OnSale = section.OnSale;
        plan.Hidden = section.Hidden;
        if (tier == ServiceTier.Custom)
        {
            plan.StorageQuotaBytes = (section.QuotaMb ?? CustomDefaultQuotaMb) * BytesPerMiB;
            plan.IncludedSlots = section.IncludedSlots ?? CustomDefaultSlots;
        }
        else
        {
            plan.StorageQuotaBytes = GetStorageQuotaBytes(tier);
            plan.IncludedSlots = GetIncludedSlots(tier);
        }
        Dictionary<int, long> prices = new Dictionary<int, long>();
        foreach (int years in saleYears)
        {
            long price = section.Get(years);
            if (price > 0)
            {
                prices[years] = price;
            }
        }
        plan.Prices = prices;
        return plan;
    }

    private static List<int> ResolveSaleYears(BillingPricingSection pricing)
    {
        List<int> years = new List<int>();
        IEnumerable<int> source = pricing.SaleYears ?? new List<int>(DefaultSaleYears);
        foreach (int value in source)
        {
            if (!years.Contains(value))
            {
                years.Add(value);
            }
        }
        years.Sort();
        return years;
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
        if (saleYears.Count == 0)
        {
            throw new InvalidOperationException("Phải có ít nhất một kỳ bán.");
        }
        foreach (int years in saleYears)
        {
            if (years < 1 || years > MaxSaleYears)
            {
                throw new InvalidOperationException("Kỳ bán phải từ 1 đến " + MaxSaleYears + " năm.");
            }
        }

        // Gói đã xóa (ẩn) không bán nữa nên không bắt buộc có giá hay xếp thứ tự giá.
        List<PlanInfo> selling = new List<PlanInfo>();
        foreach (PlanInfo plan in plans)
        {
            BillingTierPricesSection section = SectionOfPlan(plan);
            if (section.QuotaMb != null && section.QuotaMb.Value <= 0)
            {
                throw new InvalidOperationException("Gói " + plan.Name + " phải có dung lượng lớn hơn 0.");
            }
            if (section.IncludedSlots != null && section.IncludedSlots.Value < 0)
            {
                throw new InvalidOperationException("Gói " + plan.Name + " không được có số suất người nhận âm.");
            }
            if (plan.Hidden)
            {
                continue;
            }
            foreach (int years in saleYears)
            {
                if (section.Get(years) <= 0)
                {
                    throw new InvalidOperationException(
                        "Gói " + plan.Name + " thiếu giá cho kỳ " + years + " năm (phải lớn hơn 0).");
                }
            }
            selling.Add(plan);
        }

        // Gói đứng sau (cao hơn) không được rẻ hơn gói đứng trước cùng kỳ, nếu không giá nâng hạng sẽ âm.
        for (int i = 1; i < selling.Count; i++)
        {
            foreach (int years in saleYears)
            {
                if (selling[i].Prices[years] < selling[i - 1].Prices[years])
                {
                    throw new InvalidOperationException(
                        "Giá gói " + selling[i].Name + " không được thấp hơn gói " + selling[i - 1].Name
                        + " cùng kỳ (kỳ " + years + " năm).");
                }
            }
        }
    }

    private BillingTierPricesSection SectionOfPlan(PlanInfo plan)
    {
        if (plan.Tier == ServiceTier.Xs)
        {
            return pricing.Xs;
        }
        if (plan.Tier == ServiceTier.XsMax)
        {
            return pricing.XsMax;
        }
        return pricing.Custom[plan.Code];
    }
}
