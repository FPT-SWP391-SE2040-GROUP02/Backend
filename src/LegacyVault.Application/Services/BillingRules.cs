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
    /// Free → PURCHASE; PaidActive còn hạn → ADDON; còn lại (gia hạn, ân hạn, chờ rà soát) chưa hỗ trợ.
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
        throw NotAllowed(PaymentNotAllowedReason.RenewalNotSupported,
            "Kho đang ở trạng thái cần gia hạn dịch vụ. Chức năng gia hạn chưa được hỗ trợ.");
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
    /// Hạn của đơn (ADDON-03): PURCHASE = lúc tạo + 15 phút; ADDON = mốc sớm hơn giữa lúc tạo + 15 phút và hết kỳ hiện tại.
    /// </summary>
    public static DateTimeOffset ComputeOrderExpiresAt(
        PaymentOrderKind kind, DateTimeOffset now, DateTimeOffset? serviceExpiresAt)
    {
        DateTimeOffset byLifetime = now + BillingPrices.OrderLifetime;
        if (kind == PaymentOrderKind.Addon && serviceExpiresAt != null)
        {
            if (serviceExpiresAt.Value < byLifetime)
            {
                return serviceExpiresAt.Value;
            }
        }
        return byLifetime;
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

    public static AppException NotAllowed(PaymentNotAllowedReason reason, string message)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["reason"] = reason.ToApiString();
        return new AppException(AppErrorType.Conflict, ErrorCode.PaymentNotAllowed, message, extensions);
    }
}
