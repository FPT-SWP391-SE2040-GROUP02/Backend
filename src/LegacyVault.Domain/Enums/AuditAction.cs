namespace LegacyVault.Domain.Enums;

/// <summary>
/// Loại thao tác ghi vào nhật ký (SEC-07, SEC-13). Lưu dạng chữ nên chỉ thêm giá trị mới ở cuối, không đổi tên giá trị cũ.
/// </summary>
public enum AuditAction
{
    /// <summary>Owner bấm "Tạo kho của tôi" lần đầu (SETUP-12).</summary>
    VaultCreated = 0,
    /// <summary>Owner đổi tên hoặc mô tả kho.</summary>
    VaultUpdated = 1,
    /// <summary>Owner tải lên một tài sản mới (ASSET-01).</summary>
    AssetCreated = 2,
    /// <summary>Owner tạo gói (SETUP-04).</summary>
    ContainerCreated = 3,
    /// <summary>Owner đổi tên hoặc mô tả gói (SETUP-04).</summary>
    ContainerUpdated = 4,
    /// <summary>Owner lưu trữ gói (SETUP-16).</summary>
    ContainerArchived = 5,
    /// <summary>Owner thêm tài sản vào gói (SETUP-04).</summary>
    ContainerAssetAdded = 6,
    /// <summary>Owner gỡ tài sản khỏi gói (SETUP-04).</summary>
    ContainerAssetRemoved = 7,
    /// <summary>Owner chỉ định người nhận cho gói (SETUP-04).</summary>
    ContainerBeneficiaryAdded = 8,
    /// <summary>Owner gỡ người nhận khỏi gói (SETUP-04).</summary>
    ContainerBeneficiaryRemoved = 9,
    /// <summary>Kế hoạch về DRAFT vì mất gói READY cuối cùng, Owner đã xác nhận (SETUP-16).</summary>
    PlanDeactivated = 10,
    /// <summary>Owner tạo hồ sơ người nhận (QUOTA-04).</summary>
    BeneficiaryCreated = 11,
    /// <summary>Owner sửa hồ sơ người nhận.</summary>
    BeneficiaryUpdated = 12,
    /// <summary>Owner xóa hồ sơ người nhận không còn ở gói nào.</summary>
    BeneficiaryDeleted = 13,
    /// <summary>Owner điểm danh (F2A).</summary>
    CheckedIn = 14,
    /// <summary>Owner đổi chu kỳ hoặc thời gian chờ điểm danh (DMS-01).</summary>
    CheckInSettingsUpdated = 15,
}
