using System.Security.Cryptography;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật báo giá và điều kiện tạo đơn thanh toán (SRS §8.1–8.3). Lớp tĩnh, không truy cập DB;
/// backend tự tính giá và tự xác định loại đơn, không tin dữ liệu tiền hay trạng thái từ client (PAY-11).
/// </summary>
public static class BillingRules
{
    /// <summary>Bảng ký tự mã thanh toán: bỏ 0, O, 1, I để người chuyển khoản không nhầm.</summary>
    public const string PaymentCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const string PaymentCodePrefix = "LV";

    public const int PaymentCodeRandomLength = 8;

    /// <summary>
    /// Backend tự xác định loại đơn từ quyền dịch vụ của kho (§8.3):
    /// Free → PURCHASE; kho đã hết hạn, ân hạn hoặc chờ rà soát → RENEWAL; kho PaidActive còn hạn thì gia hạn sớm
    /// (renew = true) là RENEWAL, chọn gói đứng sau gói đang dùng là UPGRADE. Kho còn hạn mà không gia hạn cũng không
    /// nâng hạng thì không có gì để mua: 409 PAYMENT_NOT_ALLOWED (NOTHING_TO_BUY).
    /// </summary>
    public static PaymentOrderKind DetermineKind(
        IBillingPriceSettings prices, VaultEntitlement entitlement, string? requestedTierText, bool renew,
        DateTimeOffset now)
    {
        if (entitlement.Status == EntitlementStatus.Free)
        {
            return PaymentOrderKind.Purchase;
        }
        bool activeInTerm = entitlement.Status == EntitlementStatus.PaidActive
            && entitlement.ServiceExpiresAt != null
            && entitlement.ServiceExpiresAt.Value > now;
        if (!activeInTerm)
        {
            // Hết hạn, ân hạn hoặc chờ rà soát (kể cả PaidActive đã quá hạn khi chưa có job chuyển trạng thái): chỉ còn gia hạn.
            return PaymentOrderKind.Renewal;
        }
        if (renew)
        {
            return PaymentOrderKind.Renewal;
        }
        if (FindUpgradeTarget(prices, entitlement, requestedTierText) != null)
        {
            return PaymentOrderKind.Upgrade;
        }
        throw NotAllowed(PaymentNotAllowedReason.NothingToBuy,
            "Kho đang có dịch vụ còn hạn. Bạn chỉ có thể gia hạn sớm hoặc nâng lên gói cao hơn.");
    }

    /// <summary>Gói đích khi yêu cầu là nâng hạng; null nếu không phải: gói lạ, cùng gói hoặc gói thấp hơn.</summary>
    public static PlanInfo? FindUpgradeTarget(
        IBillingPriceSettings prices, VaultEntitlement entitlement, string? requestedTierText)
    {
        if (string.IsNullOrWhiteSpace(requestedTierText))
        {
            return null;
        }
        PlanInfo? target = prices.FindPlan(requestedTierText);
        PlanInfo? current = prices.FindPlan(PlanCodes.Resolve(entitlement.Tier, entitlement.PlanCode));
        if (target == null || current == null || target.Code == current.Code || target.Order <= current.Order)
        {
            return null;
        }
        return target;
    }

    /// <summary>
    /// Kho đã đóng, hoặc kế hoạch đang chứng tử/bàn giao/bị chặn thì không tạo đơn mới (§8.3).
    /// Cần vault đã nạp Plan.
    /// </summary>
    public static void EnsureVaultAndPlanAllowPayment(Vault vault)
    {
        if (vault.Status == VaultStatus.Closed)
        {
            throw NotAllowed(PaymentNotAllowedReason.VaultClosed, "Kho đã đóng nên không thể thanh toán.");
        }
        PlanStatus planStatus = vault.Plan.Status;
        if (planStatus == PlanStatus.HandoverRequested
            || planStatus == PlanStatus.HandoverInProgress
            || planStatus == PlanStatus.Blocked)
        {
            throw NotAllowed(PaymentNotAllowedReason.PlanLocked,
                "Kho đang trong quy trình bàn giao nên chưa thể tạo đơn mới.");
        }
    }

    /// <summary>Mua mới cần lượt eKYC Owner đã đạt và hoàn tất trong 90 ngày gần nhất (§8.3).</summary>
    public static bool IsOwnerEkycFresh(EkycVerification? verification, DateTimeOffset now)
    {
        if (verification == null)
        {
            return false;
        }
        if (verification.Status != EkycStatus.Passed)
        {
            return false;
        }
        if (verification.CompletedAt == null)
        {
            return false;
        }
        DateTimeOffset oldestAccepted = now - BillingPrices.OwnerEkycValidity;
        return verification.CompletedAt.Value >= oldestAccepted;
    }

    /// <summary>Báo giá mua mới: tổng = giá hạng của kỳ được chọn.</summary>
    public static BillingQuote QuotePurchase(IBillingPriceSettings prices, string? tierText, int? years)
    {
        PlanInfo plan = ParseSellablePlan(prices, tierText);
        if (!plan.CanBuy)
        {
            throw NotAllowed(PaymentNotAllowedReason.TierNotOnSale,
                "Gói này đang tạm ngừng mở bán. Vui lòng chọn gói khác hoặc quay lại sau.");
        }
        if (years == null)
        {
            throw ValidationErrors.For("years", "Số năm là bắt buộc.");
        }
        long tierPrice;
        if (!plan.Prices.TryGetValue(years.Value, out tierPrice))
        {
            throw ValidationErrors.For("years",
                "Kỳ này không được bán. Số năm hợp lệ: " + string.Join(", ", plan.Prices.Keys.OrderBy(y => y)) + ".");
        }
        BillingQuote quote = new BillingQuote();
        quote.Kind = PaymentOrderKind.Purchase;
        quote.Tier = plan.Tier;
        quote.PlanCode = plan.Code;
        quote.Years = years.Value;
        quote.TierAmount = tierPrice;
        quote.TotalAmount = tierPrice;
        quote.StorageQuotaBytes = plan.StorageQuotaBytes;
        quote.IncludedBeneficiarySlots = plan.IncludedSlots;
        quote.ServiceDays = years.Value * BillingPrices.DaysPerYear;
        quote.BaseServiceExpiresAt = null;
        quote.PolicyVersion = prices.PolicyVersion;
        return quote;
    }

    /// <summary>
    /// Báo giá gia hạn: giá = giá hạng của kỳ mới.
    /// Kho còn hạn: kỳ mới nối tiếp hạn cũ (BaseServiceExpiresAt = hạn cũ) và phải giữ nguyên hạng, vì hạng được cấp ngay
    /// lúc trả: đổi hạng ở đây sẽ cho dùng hạng mới trong phần còn lại của kỳ cũ mà không đúng giá (lên hạng dùng nâng hạng;
    /// hạ hạng chờ hết kỳ). Đã hết hạn hoặc ân hạn: kỳ mới tính từ lúc thanh toán (BaseServiceExpiresAt = null) nên chọn hạng
    /// tự do; việc dữ liệu hiện có có vừa hạng mới hay không do <see cref="EnsureUsageFitsRenewal"/> kiểm tra.
    /// </summary>
    public static BillingQuote QuoteRenewal(
        IBillingPriceSettings prices, VaultEntitlement entitlement, string? tierText, int? years, DateTimeOffset now)
    {
        BillingQuote quote = QuotePurchase(prices, tierText, years);
        quote.Kind = PaymentOrderKind.Renewal;
        quote.BaseServiceExpiresAt = null;
        if (entitlement.Status == EntitlementStatus.PaidActive
            && entitlement.ServiceExpiresAt != null
            && entitlement.ServiceExpiresAt.Value > now)
        {
            quote.BaseServiceExpiresAt = entitlement.ServiceExpiresAt;
            if (quote.PlanCode != PlanCodes.Resolve(entitlement.Tier, entitlement.PlanCode))
            {
                throw ValidationErrors.For("tier",
                    "Gia hạn khi còn hạn giữ nguyên hạng hiện tại. Muốn lên hạng cao hơn hãy dùng nâng hạng; muốn hạ hạng hãy chờ hết kỳ.");
            }
        }
        return quote;
    }

    /// <summary>
    /// Gia hạn sang gói không đủ chứa dữ liệu đang dùng (dung lượng đã dùng hoặc suất người nhận đã dùng)
    /// thì không cho tạo đơn (409 PAYMENT_NOT_ALLOWED / USAGE_EXCEEDS_PLAN); không tự xóa dữ liệu để vừa hạng.
    /// </summary>
    public static void EnsureUsageFitsRenewal(BillingQuote quote, long usedBytes, int usedBeneficiarySlots)
    {
        if (usedBytes > quote.StorageQuotaBytes)
        {
            throw NotAllowed(PaymentNotAllowedReason.UsageExceedsPlan,
                "Dung lượng đang dùng vượt hạn mức của hạng được chọn. Chọn hạng lớn hơn hoặc giảm dữ liệu trước.");
        }
        if (usedBeneficiarySlots > quote.IncludedBeneficiarySlots)
        {
            throw NotAllowed(PaymentNotAllowedReason.UsageExceedsPlan,
                "Số suất người nhận đang dùng vượt số suất của gói được chọn. Chọn gói nhiều suất hơn hoặc gỡ bớt người nhận trước.");
        }
    }

    /// <summary>
    /// Cấp quyền cho đơn RENEWAL: hạng, dung lượng và suất kèm theo của đơn; trạng thái về PaidActive.
    /// Hạn mới = (hạn cũ nếu đơn tạo lúc còn hạn và hạn đó chưa qua, ngược lại lúc trả) + số năm × 365 ngày.
    /// Chỉ cấp khi quyền chưa đổi so với lúc tạo đơn: đơn nối tiếp cần đúng hạn cũ; đơn tính từ lúc trả cần kho vẫn đang
    /// hết hạn hoặc ân hạn (chưa có kỳ mới).
    /// </summary>
    public static void ApplyRenewal(VaultEntitlement entitlement, PaymentOrder order, DateTimeOffset paidAt)
    {
        if (entitlement.Status == EntitlementStatus.Free || order.Years == null)
        {
            throw NotPayable(PaymentOrderNotPayableReason.EntitlementChanged,
                "Quyền dịch vụ của kho đã thay đổi kể từ lúc tạo đơn nên không thể cấp tự động.");
        }

        DateTimeOffset start = paidAt;
        if (order.BaseServiceExpiresAt != null)
        {
            // Đơn gia hạn sớm: kỳ hiện tại phải còn nguyên như lúc tạo đơn.
            bool unchanged = entitlement.Status == EntitlementStatus.PaidActive
                && SamePlan(entitlement, order)
                && entitlement.ServiceExpiresAt != null
                && entitlement.ServiceExpiresAt.Value == order.BaseServiceExpiresAt.Value;
            if (!unchanged)
            {
                throw NotPayable(PaymentOrderNotPayableReason.EntitlementChanged,
                    "Kỳ dịch vụ của kho đã thay đổi kể từ lúc tạo đơn nên không thể cấp tự động.");
            }
            if (order.BaseServiceExpiresAt.Value > paidAt)
            {
                start = order.BaseServiceExpiresAt.Value;
            }
        }
        else
        {
            // Đơn gia hạn muộn: nếu giữa chừng kho đã có kỳ còn hạn khác thì không cấp chồng.
            bool activeNow = entitlement.Status == EntitlementStatus.PaidActive
                && entitlement.ServiceExpiresAt != null
                && entitlement.ServiceExpiresAt.Value > paidAt;
            if (activeNow)
            {
                throw NotPayable(PaymentOrderNotPayableReason.EntitlementChanged,
                    "Kho đã có kỳ dịch vụ còn hạn kể từ lúc tạo đơn nên không thể cấp tự động.");
            }
        }

        entitlement.Tier = order.Tier;
        entitlement.PlanCode = PlanCodes.Resolve(order.Tier, order.PlanCode);
        entitlement.Status = EntitlementStatus.PaidActive;
        entitlement.StorageQuotaBytes = order.StorageQuotaBytes;
        entitlement.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        entitlement.ServiceExpiresAt = start.AddDays(order.Years.Value * BillingPrices.DaysPerYear);
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>
    /// Báo giá nâng hạng giữa kỳ lên gói đứng sau: trả phần chênh giá theo thời gian còn lại của kỳ hiện tại.
    /// Giá = ceil((giá gói đích − giá gói hiện tại của kỳ N năm đã mua) × số giây còn lại / (N × 365 ngày tính bằng giây)).
    /// N lấy từ đơn mua đã trả gần nhất (<paramref name="paidYears"/>); kỳ đó không còn bán thì lấy kỳ ngắn nhất còn đủ giá cả hai
    /// gói. Số giây còn lại không vượt tổng số giây của kỳ. Tính hoàn toàn bằng số nguyên long. Hạn dịch vụ giữ nguyên; dung
    /// lượng và suất kèm theo lên theo gói đích.
    /// </summary>
    public static BillingQuote QuoteUpgrade(
        IBillingPriceSettings prices, VaultEntitlement entitlement, PlanInfo target, int? paidYears, DateTimeOffset now)
    {
        if (entitlement.ServiceExpiresAt == null)
        {
            throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported, "Kho không có kỳ dịch vụ còn hạn.");
        }
        TimeSpan remaining = entitlement.ServiceExpiresAt.Value - now;
        long remainingSeconds = remaining.Ticks / TimeSpan.TicksPerSecond;
        if (remainingSeconds < 1)
        {
            throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported, "Kỳ dịch vụ đã hết hạn.");
        }
        if (!target.CanBuy)
        {
            throw NotAllowed(PaymentNotAllowedReason.TierNotOnSale,
                "Gói này đang tạm ngừng mở bán. Vui lòng chọn gói khác hoặc quay lại sau.");
        }
        PlanInfo? current = prices.FindPlan(PlanCodes.Resolve(entitlement.Tier, entitlement.PlanCode));
        if (current == null || target.Order <= current.Order)
        {
            throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported, "Chỉ nâng được lên gói cao hơn gói đang dùng.");
        }

        // Giá chênh tính theo kỳ của đơn mua đã trả; kỳ đó không còn bán (Admin bớt kỳ) thì lấy kỳ ngắn nhất còn đủ giá cả hai gói.
        int years = 0;
        if (paidYears != null && current.Prices.ContainsKey(paidYears.Value) && target.Prices.ContainsKey(paidYears.Value))
        {
            years = paidYears.Value;
        }
        if (years == 0)
        {
            foreach (int candidate in prices.SaleYears)
            {
                if (years == 0 && current.Prices.ContainsKey(candidate) && target.Prices.ContainsKey(candidate))
                {
                    years = candidate;
                }
            }
        }
        if (years == 0)
        {
            throw NotAllowed(PaymentNotAllowedReason.TierNotOnSale, "Hiện không có kỳ nào để tính giá nâng hạng.");
        }
        long priceDifference = target.Prices[years] - current.Prices[years];
        if (priceDifference <= 0)
        {
            throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported,
                "Gói này không đắt hơn gói đang dùng ở kỳ này nên không nâng hạng được.");
        }
        long periodSeconds = BillingPrices.SecondsPerYear * years;
        if (remainingSeconds > periodSeconds)
        {
            remainingSeconds = periodSeconds;
        }
        long numerator = priceDifference * remainingSeconds;
        long price = (numerator + periodSeconds - 1) / periodSeconds;

        BillingQuote quote = new BillingQuote();
        quote.Kind = PaymentOrderKind.Upgrade;
        quote.Tier = target.Tier;
        quote.PlanCode = target.Code;
        quote.Years = null;
        quote.TierAmount = price;
        quote.TotalAmount = price;
        quote.StorageQuotaBytes = target.StorageQuotaBytes;
        quote.IncludedBeneficiarySlots = target.IncludedSlots;
        quote.ServiceDays = null;
        quote.BaseServiceExpiresAt = entitlement.ServiceExpiresAt;
        quote.PolicyVersion = prices.PolicyVersion;
        return quote;
    }

    /// <summary>
    /// Hạn của đơn: PURCHASE và RENEWAL = lúc tạo + thời gian chờ; UPGRADE = mốc sớm hơn giữa lúc tạo + thời gian chờ và hết kỳ hiện tại.
    /// </summary>
    public static DateTimeOffset ComputeOrderExpiresAt(
        PaymentOrderKind kind, DateTimeOffset now, DateTimeOffset? serviceExpiresAt)
    {
        return ComputeOrderExpiresAt(kind, now, serviceExpiresAt, BillingPrices.OrderLifetime);
    }

    /// <summary>Như trên nhưng thời gian chờ do Admin cấu hình (<see cref="IBillingPriceSettings.OrderLifetime"/>).</summary>
    public static DateTimeOffset ComputeOrderExpiresAt(
        PaymentOrderKind kind, DateTimeOffset now, DateTimeOffset? serviceExpiresAt, TimeSpan lifetime)
    {
        DateTimeOffset byLifetime = now + lifetime;
        if (kind == PaymentOrderKind.Upgrade && serviceExpiresAt != null)
        {
            if (serviceExpiresAt.Value < byLifetime)
            {
                return serviceExpiresAt.Value;
            }
        }
        return byLifetime;
    }

    /// <summary>
    /// Cấp quyền cho đơn PURCHASE (D8, PAY-08): chỉ khi quyền dịch vụ vẫn còn Free. Kỳ mới bắt đầu lúc thanh toán thành công,
    /// dài số năm × 365 ngày; dung lượng và suất kèm theo lấy từ đơn (đã chốt lúc tạo). Sai điều kiện → không cấp.
    /// </summary>
    public static void ApplyPurchase(VaultEntitlement entitlement, PaymentOrder order, DateTimeOffset paidAt)
    {
        if (entitlement.Status != EntitlementStatus.Free || order.Years == null)
        {
            throw NotPayable(PaymentOrderNotPayableReason.EntitlementChanged,
                "Quyền dịch vụ của kho đã thay đổi kể từ lúc tạo đơn nên không thể cấp tự động.");
        }

        entitlement.Tier = order.Tier;
        entitlement.PlanCode = PlanCodes.Resolve(order.Tier, order.PlanCode);
        entitlement.Status = EntitlementStatus.PaidActive;
        entitlement.StorageQuotaBytes = order.StorageQuotaBytes;
        entitlement.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        entitlement.ServiceExpiresAt = paidAt.AddDays(order.Years.Value * BillingPrices.DaysPerYear);
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>
    /// Cấp quyền cho đơn UPGRADE: đổi hạng sang hạng của đơn, nâng dung lượng và suất kèm theo, giữ nguyên hạn.
    /// Chỉ cấp khi quyền vẫn là PaidActive hạng XS, đúng kỳ lúc tạo đơn (hạn không đổi) và kỳ chưa hết.
    /// </summary>
    public static void ApplyUpgrade(VaultEntitlement entitlement, PaymentOrder order, DateTimeOffset paidAt)
    {
        // Gói hiện tại phải khác gói của đơn (nâng hạng đã được kiểm tra thứ tự gói lúc tạo đơn).
        bool unchanged = entitlement.Status == EntitlementStatus.PaidActive
            && !SamePlan(entitlement, order)
            && entitlement.ServiceExpiresAt != null
            && order.BaseServiceExpiresAt != null
            && entitlement.ServiceExpiresAt.Value == order.BaseServiceExpiresAt.Value
            && entitlement.ServiceExpiresAt.Value > paidAt;
        if (!unchanged)
        {
            throw NotPayable(PaymentOrderNotPayableReason.EntitlementChanged,
                "Hạng hoặc kỳ dịch vụ của kho đã thay đổi hoặc đã hết kể từ lúc tạo đơn nên không thể cấp tự động.");
        }

        entitlement.Tier = order.Tier;
        entitlement.PlanCode = PlanCodes.Resolve(order.Tier, order.PlanCode);
        entitlement.StorageQuotaBytes = order.StorageQuotaBytes;
        entitlement.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>Quyền dịch vụ đang ở đúng gói của đơn (so mã gói; dòng cũ chưa có mã được suy ra từ hạng).</summary>
    private static bool SamePlan(VaultEntitlement entitlement, PaymentOrder order)
    {
        return PlanCodes.Resolve(entitlement.Tier, entitlement.PlanCode) == PlanCodes.Resolve(order.Tier, order.PlanCode);
    }

    /// <summary>Đơn còn Pending nhưng đã tới hoặc qua hạn thanh toán (D7: không có job, kiểm lúc đọc/tạo/hủy/trả).</summary>
    public static bool IsExpired(PaymentOrder order, DateTimeOffset now)
    {
        return order.Status == PaymentOrderStatus.Pending && now >= order.ExpiresAt;
    }

    /// <summary>
    /// Cùng lựa chọn với báo giá mới (§8.3: cùng lựa chọn thì trả lại đơn cũ): so loại đơn, gói, số năm.
    /// </summary>
    public static bool IsSameSelection(PaymentOrder order, BillingQuote quote)
    {
        return order.Kind == quote.Kind
            && PlanCodes.Resolve(order.Tier, order.PlanCode) == PlanCodes.Resolve(quote.Tier, quote.PlanCode)
            && order.Years == quote.Years;
    }

    /// <summary>
    /// Mã thanh toán "LV" + 8 ký tự ngẫu nhiên an toàn (RandomNumberGenerator), cũng là nội dung chuyển khoản.
    /// Không dùng System.Random vì mã đoán được sẽ cho phép chuyển khoản vào đơn của người khác.
    /// </summary>
    public static string NewPaymentCode()
    {
        char[] characters = new char[PaymentCodePrefix.Length + PaymentCodeRandomLength];
        for (int i = 0; i < PaymentCodePrefix.Length; i++)
        {
            characters[i] = PaymentCodePrefix[i];
        }
        for (int i = 0; i < PaymentCodeRandomLength; i++)
        {
            int index = RandomNumberGenerator.GetInt32(PaymentCodeAlphabet.Length);
            characters[PaymentCodePrefix.Length + i] = PaymentCodeAlphabet[index];
        }
        return new string(characters);
    }

    /// <summary>Đọc gói từ mã trên API ("XS", "XS_MAX" hoặc mã gói Admin tạo); thiếu hoặc không có → 400 theo trường tier.</summary>
    private static PlanInfo ParseSellablePlan(IBillingPriceSettings prices, string? tierText)
    {
        if (string.IsNullOrWhiteSpace(tierText))
        {
            throw ValidationErrors.For("tier", "Hạng dịch vụ là bắt buộc.");
        }
        PlanInfo? plan = prices.FindPlan(tierText);
        if (plan == null)
        {
            throw ValidationErrors.For("tier", "Hạng dịch vụ không hợp lệ. Hạng đang có: "
                + string.Join(", ", prices.Plans.Where(p => !p.Hidden).Select(p => p.Code)) + ".");
        }
        return plan;
    }

    public static AppException NotPayable(PaymentOrderNotPayableReason reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.PaymentOrderNotPayable, message, extensions);
    }

    public static AppException OrderNotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.PaymentOrderNotFound, "Không tìm thấy đơn thanh toán.");
    }

    public static AppException NotAllowed(PaymentNotAllowedReason reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.PaymentNotAllowed, message, extensions);
    }
}
