using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật hết hạn dịch vụ (SRS OPLAN-01): kho đã trả phí mà hết kỳ, không có kỳ nối tiếp thì sang ân hạn chỉ đọc
/// (GRACE_READ_ONLY). Lớp tĩnh, không truy cập DB; job và công cụ thử nghiệm cùng gọi các hàm này.
/// </summary>
/// <remarks>
/// - Chặn thao tác "thêm" khi ân hạn đã có sẵn ở <see cref="VaultRules"/>; ở đây chỉ đổi trạng thái.
/// - Gia hạn trong lúc ân hạn đi qua thanh toán (RENEWAL) và đưa kho về PAID_ACTIVE; không xử lý ở đây.
/// - Không xóa dữ liệu, không đụng kế hoạch, hồ sơ chứng tử hay grant (OPLAN-02, OPLAN-06).
/// </remarks>
public static class EntitlementExpiryRules
{
    /// <summary>
    /// Kho đang PAID_ACTIVE và đã tới hoặc qua hạn dịch vụ. Gia hạn sớm nối tiếp đã dời ServiceExpiresAt ra sau
    /// lúc thanh toán (PAY-08), nên "chưa tới hạn mới" nghĩa là đang có kỳ nối tiếp và không sang ân hạn.
    /// </summary>
    public static bool ShouldStartGrace(VaultEntitlement entitlement, DateTimeOffset now)
    {
        if (entitlement.Status != EntitlementStatus.PaidActive)
        {
            return false;
        }
        if (entitlement.ServiceExpiresAt == null)
        {
            return false;
        }
        return entitlement.ServiceExpiresAt.Value <= now;
    }

    /// <summary>Chuyển sang ân hạn chỉ đọc. Hạn dịch vụ giữ nguyên làm mốc bắt đầu ân hạn.</summary>
    public static void StartGrace(VaultEntitlement entitlement, DateTimeOffset now)
    {
        entitlement.Status = EntitlementStatus.GraceReadOnly;
        entitlement.UpdatedAt = now;
    }
}
