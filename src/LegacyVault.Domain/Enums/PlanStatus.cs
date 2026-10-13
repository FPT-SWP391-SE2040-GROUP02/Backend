namespace LegacyVault.Domain.Enums;

/// <summary>
/// Trạng thái kế hoạch bàn giao (SRS §11.1, chuyển trạng thái ở §11.2).
/// </summary>
/// <remarks>Tạo kho chỉ dùng Draft; các giá trị khác khai báo sẵn cho kích hoạt, điểm danh, chứng tử, bàn giao.</remarks>
public enum PlanStatus
{
    /// <summary>Đang thiết lập; chưa điểm danh, chưa bàn giao.</summary>
    Draft = 0,
    /// <summary>Đã kích hoạt theo SETUP-01; lịch điểm danh đang chạy.</summary>
    Active = 1,
    /// <summary>Đến hạn mà Owner chưa điểm danh, đang trong thời gian chờ.</summary>
    CheckinOverdue = 2,
    /// <summary>Hết thời gian chờ; đã tạo việc cho Executor kiểm tra.</summary>
    AwaitingCheck = 3,
    /// <summary>Đã có hồ sơ chứng tử; snapshot đã ghim, chặn sửa cấu hình (SETUP-05).</summary>
    DeathCaseOpen = 4,
    /// <summary>Chứng tử đã duyệt; đang bàn giao (F3).</summary>
    HandoverInProgress = 5,
    /// <summary>Mọi lượt bàn giao đã xong.</summary>
    Closed = 6,
    /// <summary>Bị chặn; lưu trạng thái trước và các nguyên nhân chặn riêng.</summary>
    Blocked = 7,
}
