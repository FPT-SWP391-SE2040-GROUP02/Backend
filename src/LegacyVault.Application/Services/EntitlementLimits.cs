using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Dung lượng hiệu lực của một kho. Kho hạng Free luôn theo cấu hình Admin hiện hành
/// (Plans.Free.QuotaMb) nên Admin đổi là áp dụng cả cho kho Free đang có; kho trả phí giữ số đã chốt lúc mua.
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
}
