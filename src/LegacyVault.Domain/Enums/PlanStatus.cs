namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái kế hoạch bàn giao (SRS §11.1, chuyển trạng thái ở §11.2).
/// </summary>
/// <remarks>
/// Tạo kho chỉ dùng Draft. Số 2 và 3 (quá hạn điểm danh, chờ kiểm tra) đã bỏ cùng điểm danh; dòng cũ được migration
/// RemoveCheckInAndExtraMail chuyển về Active. Không dùng lại hai số này.
/// </remarks>
public enum PlanStatus
{
    /// <summary>Đang thiết lập; chưa bàn giao.</summary>
    Draft = 0,
    /// <summary>Đã kích hoạt theo SETUP-01.</summary>
    Active = 1,
    /// <summary>Đã có hồ sơ chứng tử; snapshot đã ghim, chặn sửa cấu hình (SETUP-05).</summary>
    DeathCaseOpen = 4,
    /// <summary>Chứng tử đã duyệt; đang bàn giao (F3).</summary>
    HandoverInProgress = 5,
    /// <summary>Mọi lượt bàn giao đã xong.</summary>
    Closed = 6,
    /// <summary>Bị chặn; lưu trạng thái trước và các nguyên nhân chặn riêng.</summary>
    Blocked = 7,
}
