using System.Text.Json;
using System.Text.RegularExpressions;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Infrastructure.Billing;

/// <summary>
/// Quy ước nối các tham số "Plans.*" trong bảng SystemSettings với bảng giá "Billing:Pricing".
/// Chỉ dòng do Admin đã sửa (UpdatedBy khác null) và đang bật mới đè lên cấu hình; dòng mặc định tạo sẵn thì không,
/// để giá đặt trong appsettings hoặc user-secrets vẫn có tác dụng khi Admin chưa chỉnh.
/// </summary>
/// <remarks>
/// Khóa Admin điều khiển:
/// Plans.{Gói}.Price{N}Y (giá kỳ N năm, N từ 1 đến 10), Plans.{Gói}.On (mở bán), Plans.{Gói}.Hidden (đã xóa),
/// Plans.{Gói}.QuotaMb (dung lượng), Plans.{Gói}.Slots (suất người nhận kèm), với {Gói} là Xs, XsMax hoặc mã gói Admin tạo;
/// Plans.Periods (danh sách kỳ bán, JSON), Plans.Catalog (danh sách gói Admin tạo thêm, JSON), Plans.Addon.SlotPricePerYear.
/// </remarks>
public static class PlanPriceRules
{
    public const string PlanKeyPrefix = "Plans.";

    /// <summary>Khóa lưu danh sách kỳ bán: mảng JSON [{"years":1,"on":true}, ...].</summary>
    public const string PeriodsKey = "Plans.Periods";

    /// <summary>Khóa lưu các gói Admin tạo thêm: mảng JSON [{"code":"GOLD","name":"Gold","subtitle":"","order":30}, ...].</summary>
    public const string CatalogKey = "Plans.Catalog";

    /// <summary>Thứ tự của hai gói gốc; gói thêm dùng số lớn hơn để đứng sau.</summary>
    public const int XsOrder = 10;

    public const int XsMaxOrder = 20;

    /// <summary>Tiền tố PolicyVersion khi giá đang bị Admin đè. Cột PolicyVersion của đơn dài tối đa 16 ký tự.</summary>
    private const string PolicyVersionPrefix = "DB-";

    /// <summary>Đoạn tên không phải mã gói (gói Free, suất thêm, danh sách kỳ và danh sách gói).</summary>
    private static readonly string[] ReservedSegments = new string[] { "Free", "Addon", "Periods", "Catalog" };

    private static readonly Regex PlanKeyPattern = new Regex(
        @"^Plans\.([A-Za-z][A-Za-z0-9_]{0,31})\.(?:Price(\d{1,2})Y|(On|Hidden|QuotaMb|Slots))$",
        RegexOptions.CultureInvariant);

    /// <summary>Mã gói Admin đặt: chữ hoa, số, gạch dưới, bắt đầu bằng chữ cái; không trùng mã gốc.</summary>
    private static readonly Regex CustomCodePattern = new Regex(@"^[A-Z][A-Z0-9_]{1,23}$", RegexOptions.CultureInvariant);

    private static readonly string[] ReservedCodes = new string[] { "FREE", "XS", "XS_MAX", "XSMAX", "CUSTOM" };

    /// <summary>Kết quả tách một khóa Plans.{Gói}.{Trường}; null nếu khóa không thuộc gói nào.</summary>
    public sealed class PlanKey
    {
        public string Plan { get; set; } = string.Empty;

        /// <summary>Price, On, Hidden, QuotaMb hoặc Slots.</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Số năm khi Field là Price.</summary>
        public int Years { get; set; }
    }

    public static PlanKey? ParsePlanKey(string key)
    {
        Match match = PlanKeyPattern.Match(key);
        if (!match.Success)
        {
            return null;
        }
        string plan = match.Groups[1].Value;
        if (Array.IndexOf(ReservedSegments, plan) >= 0)
        {
            return null;
        }
        PlanKey result = new PlanKey();
        result.Plan = plan;
        if (match.Groups[2].Success)
        {
            result.Field = "Price";
            result.Years = int.Parse(match.Groups[2].Value);
        }
        else
        {
            result.Field = match.Groups[3].Value;
        }
        return result;
    }

    public static bool IsCustomCode(string code)
    {
        return CustomCodePattern.IsMatch(code) && Array.IndexOf(ReservedCodes, code) < 0;
    }

    public static bool IsPriceKey(string key)
    {
        PlanKey? parsed = ParsePlanKey(key);
        return (parsed != null && parsed.Field == "Price") || key == "Plans.Addon.SlotPricePerYear";
    }

    /// <summary>Khóa hạn mức của gói: dung lượng (MiB) hoặc số suất người nhận kèm theo.</summary>
    public static bool IsLimitKey(string key)
    {
        PlanKey? parsed = ParsePlanKey(key);
        return parsed != null && (parsed.Field == "QuotaMb" || parsed.Field == "Slots");
    }

    /// <summary>Khóa cờ true/false của gói: mở bán hoặc đã xóa.</summary>
    public static bool IsFlagKey(string key)
    {
        PlanKey? parsed = ParsePlanKey(key);
        return parsed != null && (parsed.Field == "On" || parsed.Field == "Hidden");
    }

    /// <summary>Khóa ảnh hưởng tới bảng giá, kỳ bán, danh sách gói hoặc hạn mức, nên phải kiểm tra bảng giá sau khi ghép trước khi lưu.</summary>
    public static bool IsPricingKey(string key)
    {
        return IsPriceKey(key) || IsLimitKey(key) || IsFlagKey(key) || key == PeriodsKey || key == CatalogKey;
    }

    /// <summary>Số năm của khóa giá kỳ; null nếu khóa không phải giá kỳ.</summary>
    public static int? PriceKeyYears(string key)
    {
        PlanKey? parsed = ParsePlanKey(key);
        if (parsed == null || parsed.Field != "Price")
        {
            return null;
        }
        return parsed.Years;
    }

    /// <summary>Đọc mảng kỳ bán; trả null nếu không phải JSON đúng dạng. Chỉ giữ các kỳ đang bật.</summary>
    public static List<int>? ParsePeriods(string json, out List<int> allYears)
    {
        allYears = new List<int>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            List<int> onYears = new List<int>();
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                JsonElement years;
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("years", out years)
                    || years.ValueKind != JsonValueKind.Number || !years.TryGetInt32(out int value))
                {
                    return null;
                }
                bool on = true;
                JsonElement onElement;
                if (item.TryGetProperty("on", out onElement) && onElement.ValueKind == JsonValueKind.False)
                {
                    on = false;
                }
                allYears.Add(value);
                if (on)
                {
                    onYears.Add(value);
                }
            }
            return onYears;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Đọc danh sách gói Admin tạo thêm; trả null nếu không phải JSON đúng dạng hoặc có mã không hợp lệ.</summary>
    public static List<BillingPlanSection>? ParseCatalog(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            List<BillingPlanSection> plans = new List<BillingPlanSection>();
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                BillingPlanSection plan = new BillingPlanSection();
                plan.Code = ReadString(item, "code");
                plan.Name = ReadString(item, "name").Trim();
                plan.Subtitle = ReadString(item, "subtitle").Trim();
                JsonElement order;
                if (item.TryGetProperty("order", out order) && order.ValueKind == JsonValueKind.Number
                    && order.TryGetInt32(out int orderValue))
                {
                    plan.Order = orderValue;
                }
                else
                {
                    plan.Order = XsMaxOrder + 10 * (plans.Count + 1);
                }
                if (!IsCustomCode(plan.Code) || plan.Name.Length == 0 || plan.Name.Length > 40 || plan.Subtitle.Length > 60)
                {
                    return null;
                }
                plans.Add(plan);
            }
            return plans;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement item, string name)
    {
        JsonElement value;
        if (item.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static BillingTierPricesSection? SectionFor(BillingPricingSection pricing, string plan, bool create)
    {
        if (plan == "Xs")
        {
            return pricing.Xs;
        }
        if (plan == "XsMax")
        {
            return pricing.XsMax;
        }
        BillingPlanSection? custom;
        if (pricing.Custom.TryGetValue(plan, out custom))
        {
            return custom;
        }
        if (!create)
        {
            return null;
        }
        custom = new BillingPlanSection();
        custom.Code = plan;
        pricing.Custom[plan] = custom;
        return custom;
    }

    /// <summary>Gán giá trị vào đúng ô của bảng giá theo tên tham số. Trả false nếu tên không phải tham số giá hoặc giá trị sai dạng.</summary>
    public static bool TryApply(BillingPricingSection pricing, string key, string value)
    {
        PlanKey? parsed = ParsePlanKey(key);
        if (parsed != null)
        {
            BillingTierPricesSection section = SectionFor(pricing, parsed.Plan, true)!;
            if (parsed.Field == "On" || parsed.Field == "Hidden")
            {
                bool flag;
                if (!bool.TryParse(value, out flag))
                {
                    return false;
                }
                if (parsed.Field == "On")
                {
                    section.OnSale = flag;
                }
                else
                {
                    section.Hidden = flag;
                }
                return true;
            }

            long amount;
            if (!long.TryParse(value, out amount))
            {
                return false;
            }
            if (parsed.Field == "Price")
            {
                section.Set(parsed.Years, amount);
                return true;
            }
            if (amount < 0 || amount > int.MaxValue)
            {
                return false;
            }
            if (parsed.Field == "QuotaMb")
            {
                section.QuotaMb = amount;
            }
            else
            {
                section.IncludedSlots = (int)amount;
            }
            return true;
        }
        if (key == "Plans.Addon.SlotPricePerYear")
        {
            long amount;
            if (!long.TryParse(value, out amount))
            {
                return false;
            }
            pricing.SlotPricePerYear = amount;
            return true;
        }
        if (key == PeriodsKey)
        {
            List<int> allYears;
            List<int>? onYears = ParsePeriods(value, out allYears);
            if (onYears == null)
            {
                return false;
            }
            pricing.SaleYears = onYears;
            return true;
        }
        if (key == CatalogKey)
        {
            List<BillingPlanSection>? declared = ParseCatalog(value);
            if (declared == null)
            {
                return false;
            }
            foreach (BillingPlanSection plan in declared)
            {
                BillingPlanSection target = (BillingPlanSection)SectionFor(pricing, plan.Code, true)!;
                target.Code = plan.Code;
                target.Name = plan.Name;
                target.Subtitle = plan.Subtitle;
                target.Order = plan.Order;
            }
            return true;
        }
        return false;
    }

    public static BillingPricingSection Clone(BillingPricingSection source)
    {
        BillingPricingSection copy = new BillingPricingSection();
        copy.PolicyVersion = source.PolicyVersion;
        copy.SlotPricePerYear = source.SlotPricePerYear;
        CopyTier(source.Xs, copy.Xs);
        CopyTier(source.XsMax, copy.XsMax);
        copy.SaleYears = source.SaleYears == null ? null : new List<int>(source.SaleYears);
        foreach (KeyValuePair<string, BillingPlanSection> pair in source.Custom)
        {
            BillingPlanSection plan = new BillingPlanSection();
            plan.Code = pair.Value.Code;
            plan.Name = pair.Value.Name;
            plan.Subtitle = pair.Value.Subtitle;
            plan.Order = pair.Value.Order;
            CopyTier(pair.Value, plan);
            copy.Custom[pair.Key] = plan;
        }
        return copy;
    }

    private static void CopyTier(BillingTierPricesSection from, BillingTierPricesSection to)
    {
        to.OneYear = from.OneYear;
        to.ThreeYears = from.ThreeYears;
        to.FiveYears = from.FiveYears;
        to.QuotaMb = from.QuotaMb;
        to.IncludedSlots = from.IncludedSlots;
        to.OnSale = from.OnSale;
        to.Hidden = from.Hidden;
        to.Extra = new Dictionary<int, long>(from.Extra);
    }

    /// <summary>
    /// Bảng giá đang hiệu lực = cấu hình gốc + các dòng giá, kỳ bán, gói và cờ Admin đã sửa. hasOverride=false nghĩa là
    /// không có dòng nào đè (PolicyVersion giữ nguyên của cấu hình gốc); ngược lại PolicyVersion đổi theo lần sửa gần nhất (PAY-11).
    /// </summary>
    public static BillingPricingSection BuildEffective(
        BillingPricingSection basePricing, List<SystemSetting> rows, out bool hasOverride)
    {
        BillingPricingSection effective = Clone(basePricing);
        hasOverride = false;
        DateTimeOffset latestUpdate = DateTimeOffset.MinValue;

        // Danh sách gói thêm phải được áp trước các khóa riêng của gói, để tên và thứ tự không bị bỏ sót.
        List<SystemSetting> ordered = new List<SystemSetting>(rows);
        ordered.Sort((a, b) => (a.Key == CatalogKey ? 0 : 1).CompareTo(b.Key == CatalogKey ? 0 : 1));

        foreach (SystemSetting row in ordered)
        {
            if (row.UpdatedBy == null)
            {
                continue;
            }

            string value = row.Value;
            if (!row.IsEnabled)
            {
                // Dòng mở bán bị tắt nghĩa là gói ngừng bán; các dòng khác bị tắt thì không đè cấu hình.
                PlanKey? disabled = ParsePlanKey(row.Key);
                if (disabled == null || disabled.Field != "On")
                {
                    continue;
                }
                value = "false";
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
