using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Dung lượng và số suất người nhận hiệu lực của một kho. Kho hạng Free luôn theo cấu hình Admin hiện hành
/// (Plans.Free.QuotaMb, Plans.Free.Slots) nên Admin đổi là áp dụng cả cho kho Free đang có; kho trả phí giữ số đã chốt lúc mua.
/// </summary>
public static class EntitlementLimits
{
    public static long EffectiveQuotaBytes(VaultEntitlement entitlement)
    {
        if (entitlement.Status == EntitlementStatus.Free)
        {
            return StorageLimits.FreeQuotaBytes;
        }
        return entitlement.StorageQuotaBytes;
    }

    public static int EffectiveIncludedSlots(VaultEntitlement entitlement)
    {
        if (entitlement.Status == EntitlementStatus.Free)
        {
            return RuntimeSettings.NonNegativeInt("Plans.Free.Slots", 0);
        }
        return entitlement.IncludedBeneficiarySlots;
    }

    public static int EffectiveTotalSlots(VaultEntitlement entitlement)
    {
        return EffectiveIncludedSlots(entitlement) + entitlement.ExtraBeneficiarySlots;
    }
}
