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

    /// <summary>Số gói trả phí tối đa chưa bị xóa; cộng gói Free là bốn gói.</summary>
    public const int MaxPaidPlans = 3;

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

    public int MaxExtraSlotsPerOrder
    {
        get { return pricing.MaxExtraSlotsPerOrder ?? BillingPrices.MaxExtraSlotsPerOrder; }
    }

    public TimeSpan OrderLifetime
    {
        get
        {
            if (pricing.OrderTimeoutMinutes == null)
            {
                return BillingPrices.OrderLifetime;
            }
            return TimeSpan.FromMinutes(pricing.OrderTimeoutMinutes.Value);
        }
    }

    public IReadOnlyList<PlanInfo> Plans
    {
        get { return plans; }
    }

    public IReadOnlyList<int> SaleYears
    {
        get { return saleYears; }
    }

    public IReadOnlyList<string> FreeFeatures
    {
        get { return pricing.FreeFeatures ?? new List<string>(); }
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
        RankByPrice(result);
        return result;
    }

    /// <summary>
    /// Xếp gói từ rẻ đến đắt theo giá kỳ bán ngắn nhất rồi đánh lại thứ tự (10, 20, 30...), nên Admin đặt giá thế nào
    /// thì gói rẻ đứng trước, gói đắt đứng sau; nâng hạng luôn đi từ gói rẻ lên gói đắt hơn. Giá bằng nhau giữ thứ tự cũ;
    /// gói chưa có giá ở kỳ đó (gói đã xóa) xếp cuối.
    /// </summary>
    private void RankByPrice(List<PlanInfo> ranked)
    {
        if (saleYears.Count == 0)
        {
            return;
        }
        int rankYear = saleYears[0];
        List<PlanInfo> original = new List<PlanInfo>(ranked);
        ranked.Sort((a, b) =>
        {
            long left = a.Prices.ContainsKey(rankYear) ? a.Prices[rankYear] : long.MaxValue;
            long right = b.Prices.ContainsKey(rankYear) ? b.Prices[rankYear] : long.MaxValue;
            int byPrice = left.CompareTo(right);
            return byPrice != 0 ? byPrice : original.IndexOf(a).CompareTo(original.IndexOf(b));
        });
        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Order = (i + 1) * 10;
        }
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
        plan.Features = section.Features ?? new List<string>();
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
        if (MaxExtraSlotsPerOrder < 1)
        {
            throw new InvalidOperationException("Số suất thêm tối đa mỗi đơn phải lớn hơn 0.");
        }
        if (OrderLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Hạn đơn chờ thanh toán phải lớn hơn 0 phút.");
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

        // Tối đa bốn gói trên bảng giá tính cả Free, nên tối đa 3 gói trả phí chưa bị xóa.
        if (selling.Count > MaxPaidPlans)
        {
            throw new InvalidOperationException(
                "Tối đa " + (MaxPaidPlans + 1) + " gói (tính cả Free): hiện có " + selling.Count + " gói trả phí đang hiện. Xóa bớt một gói.");
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
