using System.Security.Cryptography;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Mappings;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật báo giá và điều kiện tạo đơn thanh toán (SRS §8.1–8.3). Lớp tĩnh, không truy cập DB;
/// backend tự tính giá và tự xác định loại đơn, không tin dữ liệu tiền hay trạng thái từ client (ADDON-01, PAY-11).
/// </summary>
public static class BillingRules
{
    /// <summary>Bảng ký tự mã thanh toán: bỏ 0, O, 1, I để người chuyển khoản không nhầm.</summary>
    public const string PaymentCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const string PaymentCodePrefix = "LV";

    public const int PaymentCodeRandomLength = 8;

    /// <summary>
    /// Backend tự xác định loại đơn từ quyền dịch vụ của kho (§8.3):
    /// Free → PURCHASE; PaidActive còn hạn → ADDON; còn lại (hết hạn, ân hạn, chờ rà soát) → RENEWAL.
    /// </summary>
    public static PaymentOrderKind DetermineKind(VaultEntitlement entitlement, DateTimeOffset now)
    {
        if (entitlement.Status == EntitlementStatus.Free)
        {
            return PaymentOrderKind.Purchase;
        }
        if (entitlement.Status == EntitlementStatus.PaidActive
            && entitlement.ServiceExpiresAt != null
            && entitlement.ServiceExpiresAt.Value > now)
        {
            return PaymentOrderKind.Addon;
        }
        // Hết hạn, ân hạn hoặc chờ rà soát (kể cả PaidActive đã quá hạn khi chưa có job chuyển trạng thái): chỉ còn gia hạn.
        return PaymentOrderKind.Renewal;
    }

    /// <summary>
    /// Như bản không có lựa chọn: kho PaidActive còn hạn đang ở XS mà client chọn XS_MAX và không mua thêm suất
    /// (extraSlots = 0) thì là nâng hạng giữa kỳ (UPGRADE); mọi lựa chọn khác vẫn là ADDON (hạng và số năm client gửi bị
    /// bỏ qua như trước, ví dụ XS_MAX kèm extraSlots = 1 vẫn là mua thêm suất).
    /// </summary>
    public static PaymentOrderKind DetermineKind(
        VaultEntitlement entitlement, string? requestedTierText, int extraSlots, DateTimeOffset now)
    {
        return DetermineKind(entitlement, requestedTierText, extraSlots, false, now);
    }

    /// <summary>
    /// Như bản trên, thêm cờ gia hạn: kho PaidActive còn hạn mà client gửi renew = true thì là gia hạn sớm (RENEWAL).
    /// Kho đã hết hạn, ân hạn hoặc chờ rà soát luôn là RENEWAL. Free luôn là PURCHASE.
    /// </summary>
    public static PaymentOrderKind DetermineKind(
        VaultEntitlement entitlement, string? requestedTierText, int extraSlots, bool renew, DateTimeOffset now)
    {
        PaymentOrderKind kind = DetermineKind(entitlement, now);
        if (kind == PaymentOrderKind.Addon)
        {
            if (renew)
            {
                return PaymentOrderKind.Renewal;
            }
            if (IsUpgradeRequest(entitlement, requestedTierText, extraSlots))
            {
                return PaymentOrderKind.Upgrade;
            }
        }
        return kind;
    }

    /// <summary>
    /// Chỉ có một đường nâng hạng: kho đang ở XS chọn XS_MAX và không mua thêm suất. Mua thêm suất cần extraSlots từ 1
    /// nên extraSlots = 0 không thể là mua thêm suất. Hạ hạng hoặc chọn cùng hạng không phải nâng hạng.
    /// </summary>
    public static bool IsUpgradeRequest(VaultEntitlement entitlement, string? requestedTierText, int extraSlots)
    {
        if (extraSlots != 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requestedTierText))
        {
            return false;
        }
        if (entitlement.Tier != ServiceTier.Xs)
        {
            return false;
        }
        string normalized = requestedTierText.Trim().ToUpperInvariant();
        return normalized == ServiceTier.XsMax.ToApiString();
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
        if (planStatus == PlanStatus.DeathCaseOpen
            || planStatus == PlanStatus.HandoverInProgress
            || planStatus == PlanStatus.Blocked)
        {
            throw NotAllowed(PaymentNotAllowedReason.PlanLocked,
                "Kế hoạch đang trong quy trình chứng tử hoặc bàn giao nên chưa thể tạo đơn mới.");
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

    /// <summary>Báo giá mua mới (ADDON-01): tổng = giá hạng + 49.000 × q × số năm.</summary>
    public static BillingQuote QuotePurchase(string? tierText, int? years, int extraSlots)
    {
        ServiceTier tier = ParseSellableTier(tierText);
        if (years == null)
        {
            throw ValidationErrors.For("years", "Số năm là bắt buộc.");
        }
        if (!BillingPrices.IsSupportedYears(years.Value))
        {
            throw ValidationErrors.For("years", "Số năm chỉ có thể là 1, 3 hoặc 5.");
        }
        if (extraSlots < 0 || extraSlots > BillingPrices.MaxExtraSlotsPerOrder)
        {
            throw ValidationErrors.For("extraSlots",
                $"Số suất mua thêm phải từ 0 đến {BillingPrices.MaxExtraSlotsPerOrder}.");
        }

        BillingQuote quote = new BillingQuote();
        quote.Kind = PaymentOrderKind.Purchase;
        quote.Tier = tier;
        quote.Years = years.Value;
        quote.ExtraSlots = extraSlots;
        quote.TierAmount = BillingPrices.GetTierPrice(tier, years.Value);
        quote.SlotAmount = BillingPrices.SlotPricePerYear * extraSlots * years.Value;
        quote.TotalAmount = quote.TierAmount + quote.SlotAmount;
        quote.StorageQuotaBytes = BillingPrices.GetStorageQuotaBytes(tier);
        quote.IncludedBeneficiarySlots = BillingPrices.GetIncludedSlots(tier);
        quote.TotalBeneficiarySlots = quote.IncludedBeneficiarySlots + extraSlots;
        quote.ServiceDays = years.Value * BillingPrices.DaysPerYear;
        quote.BaseServiceExpiresAt = null;
        return quote;
    }

    /// <summary>
    /// Báo giá mua thêm suất giữa kỳ (ADDON-02): giá = ceil(49.000 × q × số giây còn lại / 31.536.000).
    /// Tính hoàn toàn bằng số nguyên long để không sai số làm tròn.
    /// </summary>
    public static BillingQuote QuoteAddon(VaultEntitlement entitlement, int extraSlots, DateTimeOffset now)
    {
        if (extraSlots < 1 || extraSlots > BillingPrices.MaxExtraSlotsPerOrder)
        {
            throw ValidationErrors.For("extraSlots",
                $"Số suất mua thêm phải từ 1 đến {BillingPrices.MaxExtraSlotsPerOrder}.");
        }
        if (entitlement.ServiceExpiresAt == null)
        {
            throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported, "Kho không có kỳ dịch vụ còn hạn.");
        }

        // Số giây còn lại làm tròn xuống tới giây (thời lượng dương nên chia nguyên là làm tròn xuống).
        TimeSpan remaining = entitlement.ServiceExpiresAt.Value - now;
        long remainingSeconds = remaining.Ticks / TimeSpan.TicksPerSecond;
        if (remainingSeconds < 1)
        {
            throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported, "Kỳ dịch vụ đã hết hạn.");
        }

        long numerator = BillingPrices.SlotPricePerYear * extraSlots * remainingSeconds;
        long price = (numerator + BillingPrices.SecondsPerYear - 1) / BillingPrices.SecondsPerYear;

        BillingQuote quote = new BillingQuote();
        quote.Kind = PaymentOrderKind.Addon;
        quote.Tier = entitlement.Tier;
        quote.Years = null;
        quote.ExtraSlots = extraSlots;
        quote.TierAmount = 0;
        quote.SlotAmount = price;
        quote.TotalAmount = price;
        quote.StorageQuotaBytes = entitlement.StorageQuotaBytes;
        quote.IncludedBeneficiarySlots = entitlement.IncludedBeneficiarySlots;
        quote.TotalBeneficiarySlots = entitlement.IncludedBeneficiarySlots + entitlement.ExtraBeneficiarySlots + extraSlots;
        quote.ServiceDays = null;
        quote.BaseServiceExpiresAt = entitlement.ServiceExpiresAt;
        return quote;
    }

    /// <summary>
    /// Báo giá gia hạn: giá = giá hạng của kỳ mới + 49.000 × số suất thêm × số năm (suất thêm lấy theo lựa chọn, tối đa 20).
    /// Hạng được chọn tự do (cùng hạng hoặc đổi hạng); việc dữ liệu hiện có có vừa hạng mới hay không do
    /// <see cref="EnsureUsageFitsRenewal"/> kiểm tra. Kho còn hạn: kỳ mới nối tiếp hạn cũ (BaseServiceExpiresAt = hạn cũ);
    /// đã hết hạn hoặc ân hạn: kỳ mới tính từ lúc thanh toán (BaseServiceExpiresAt = null).
    /// </summary>
    public static BillingQuote QuoteRenewal(
        VaultEntitlement entitlement, string? tierText, int? years, int extraSlots, DateTimeOffset now)
    {
        BillingQuote quote = QuotePurchase(tierText, years, extraSlots);
        quote.Kind = PaymentOrderKind.Renewal;
        quote.BaseServiceExpiresAt = null;
        if (entitlement.Status == EntitlementStatus.PaidActive
            && entitlement.ServiceExpiresAt != null
            && entitlement.ServiceExpiresAt.Value > now)
        {
            quote.BaseServiceExpiresAt = entitlement.ServiceExpiresAt;
        }
        return quote;
    }

    /// <summary>
    /// Gia hạn sang hạng hoặc số suất không đủ chứa dữ liệu đang dùng (dung lượng đã dùng hoặc suất người nhận đã dùng)
    /// thì không cho tạo đơn (409 PAYMENT_NOT_ALLOWED / USAGE_EXCEEDS_PLAN); không tự xóa dữ liệu để vừa hạng.
    /// </summary>
    public static void EnsureUsageFitsRenewal(BillingQuote quote, long usedBytes, int usedBeneficiarySlots)
    {
        if (usedBytes > quote.StorageQuotaBytes)
        {
            throw NotAllowed(PaymentNotAllowedReason.UsageExceedsPlan,
                "Dung lượng đang dùng vượt hạn mức của hạng được chọn. Chọn hạng lớn hơn hoặc giảm dữ liệu trước.");
        }
        if (usedBeneficiarySlots > quote.TotalBeneficiarySlots)
        {
            throw NotAllowed(PaymentNotAllowedReason.UsageExceedsPlan,
                "Số suất người nhận đang dùng vượt số suất của lựa chọn này. Chọn thêm suất hoặc gỡ bớt người nhận trước.");
        }
    }

    /// <summary>
    /// Cấp quyền cho đơn RENEWAL: hạng, dung lượng, suất kèm theo và suất thêm theo đơn; trạng thái về PaidActive.
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
        entitlement.Status = EntitlementStatus.PaidActive;
        entitlement.StorageQuotaBytes = order.StorageQuotaBytes;
        entitlement.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        entitlement.ExtraBeneficiarySlots = order.ExtraSlots;
        entitlement.ServiceExpiresAt = start.AddDays(order.Years.Value * BillingPrices.DaysPerYear);
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>
    /// Báo giá nâng hạng giữa kỳ XS → XS_MAX: trả phần chênh giá theo thời gian còn lại của kỳ hiện tại.
    /// Giá = ceil((giá XS_MAX − giá XS của kỳ N năm đã mua) × số giây còn lại / (N × 365 ngày tính bằng giây)).
    /// N lấy từ đơn mua đã trả gần nhất (<paramref name="paidYears"/>); không có thì dùng bảng giá 1 năm. Số giây còn lại
    /// không vượt tổng số giây của kỳ. Tính hoàn toàn bằng số nguyên long. Hạn dịch vụ giữ nguyên; dung lượng và suất
    /// kèm theo lên theo XS_MAX; suất mua thêm đang có được giữ.
    /// </summary>
    public static BillingQuote QuoteUpgrade(VaultEntitlement entitlement, int? paidYears, int extraSlots, DateTimeOffset now)
    {
        if (extraSlots != 0)
        {
            throw ValidationErrors.For("extraSlots", "Nâng hạng không kèm mua thêm suất; mua thêm suất ở một đơn khác.");
        }
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

        int years = 1;
        if (paidYears != null && BillingPrices.IsSupportedYears(paidYears.Value))
        {
            years = paidYears.Value;
        }
        long priceDifference = BillingPrices.GetTierPrice(ServiceTier.XsMax, years)
            - BillingPrices.GetTierPrice(ServiceTier.Xs, years);
        long periodSeconds = BillingPrices.SecondsPerYear * years;
        if (remainingSeconds > periodSeconds)
        {
            remainingSeconds = periodSeconds;
        }
        long numerator = priceDifference * remainingSeconds;
        long price = (numerator + periodSeconds - 1) / periodSeconds;

        BillingQuote quote = new BillingQuote();
        quote.Kind = PaymentOrderKind.Upgrade;
        quote.Tier = ServiceTier.XsMax;
        quote.Years = null;
        quote.ExtraSlots = 0;
        quote.TierAmount = price;
        quote.SlotAmount = 0;
        quote.TotalAmount = price;
        quote.StorageQuotaBytes = BillingPrices.GetStorageQuotaBytes(ServiceTier.XsMax);
        quote.IncludedBeneficiarySlots = BillingPrices.GetIncludedSlots(ServiceTier.XsMax);
        quote.TotalBeneficiarySlots = quote.IncludedBeneficiarySlots + entitlement.ExtraBeneficiarySlots;
        quote.ServiceDays = null;
        quote.BaseServiceExpiresAt = entitlement.ServiceExpiresAt;
        return quote;
    }

    /// <summary>
    /// Hạn của đơn (ADDON-03): PURCHASE = lúc tạo + 15 phút; ADDON = mốc sớm hơn giữa lúc tạo + 15 phút và hết kỳ hiện tại.
    /// </summary>
    public static DateTimeOffset ComputeOrderExpiresAt(
        PaymentOrderKind kind, DateTimeOffset now, DateTimeOffset? serviceExpiresAt)
    {
        DateTimeOffset byLifetime = now + BillingPrices.OrderLifetime;
        if ((kind == PaymentOrderKind.Addon || kind == PaymentOrderKind.Upgrade) && serviceExpiresAt != null)
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
        entitlement.Status = EntitlementStatus.PaidActive;
        entitlement.StorageQuotaBytes = order.StorageQuotaBytes;
        entitlement.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        entitlement.ExtraBeneficiarySlots = order.ExtraSlots;
        entitlement.ServiceExpiresAt = paidAt.AddDays(order.Years.Value * BillingPrices.DaysPerYear);
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>
    /// Cấp quyền cho đơn ADDON (D8, ADDON-02, ADDON-03): chỉ tăng số suất thêm, giữ nguyên hạn. Chỉ cấp khi quyền vẫn là
    /// PaidActive cùng hạng, đúng kỳ lúc tạo đơn (hạn không đổi) và kỳ chưa hết; ngược lại không tự cấp.
    /// </summary>
    public static void ApplyAddon(VaultEntitlement entitlement, PaymentOrder order, DateTimeOffset paidAt)
    {
        bool unchanged = entitlement.Status == EntitlementStatus.PaidActive
            && entitlement.Tier == order.Tier
            && entitlement.ServiceExpiresAt != null
            && order.BaseServiceExpiresAt != null
            && entitlement.ServiceExpiresAt.Value == order.BaseServiceExpiresAt.Value
            && entitlement.ServiceExpiresAt.Value > paidAt;
        if (!unchanged)
        {
            throw NotPayable(PaymentOrderNotPayableReason.EntitlementChanged,
                "Kỳ dịch vụ của kho đã thay đổi hoặc đã hết kể từ lúc tạo đơn nên không thể cấp tự động.");
        }

        entitlement.ExtraBeneficiarySlots = entitlement.ExtraBeneficiarySlots + order.ExtraSlots;
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>
    /// Cấp quyền cho đơn UPGRADE: đổi hạng sang hạng của đơn, nâng dung lượng và suất kèm theo, giữ nguyên hạn và suất
    /// mua thêm. Chỉ cấp khi quyền vẫn là PaidActive hạng XS, đúng kỳ lúc tạo đơn (hạn không đổi) và kỳ chưa hết.
    /// </summary>
    public static void ApplyUpgrade(VaultEntitlement entitlement, PaymentOrder order, DateTimeOffset paidAt)
    {
        bool unchanged = entitlement.Status == EntitlementStatus.PaidActive
            && entitlement.Tier == ServiceTier.Xs
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
        entitlement.StorageQuotaBytes = order.StorageQuotaBytes;
        entitlement.IncludedBeneficiarySlots = order.IncludedBeneficiarySlots;
        entitlement.UpdatedAt = paidAt;
    }

    /// <summary>Đơn còn Pending nhưng đã tới hoặc qua hạn thanh toán (D7: không có job, kiểm lúc đọc/tạo/hủy/trả).</summary>
    public static bool IsExpired(PaymentOrder order, DateTimeOffset now)
    {
        return order.Status == PaymentOrderStatus.Pending && now >= order.ExpiresAt;
    }

    /// <summary>
    /// Cùng lựa chọn với báo giá mới (§8.3: cùng lựa chọn thì trả lại đơn cũ): so loại đơn, hạng, số năm, số suất.
    /// </summary>
    public static bool IsSameSelection(PaymentOrder order, BillingQuote quote)
    {
        return order.Kind == quote.Kind
            && order.Tier == quote.Tier
            && order.Years == quote.Years
            && order.ExtraSlots == quote.ExtraSlots;
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

    /// <summary>Đọc hạng từ chuỗi API ("XS" hoặc "XS_MAX"); thiếu hoặc không bán → 400 theo trường tier.</summary>
    private static ServiceTier ParseSellableTier(string? tierText)
    {
        if (string.IsNullOrWhiteSpace(tierText))
        {
            throw ValidationErrors.For("tier", "Hạng dịch vụ là bắt buộc.");
        }
        string normalized = tierText.Trim().ToUpperInvariant();
        for (int i = 0; i < BillingPrices.SellableTiers.Length; i++)
        {
            ServiceTier candidate = BillingPrices.SellableTiers[i];
            if (candidate.ToApiString() == normalized)
            {
                return candidate;
            }
        }
        throw ValidationErrors.For("tier", "Hạng dịch vụ chỉ có thể là XS hoặc XS_MAX.");
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
