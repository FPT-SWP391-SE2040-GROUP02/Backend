namespace LegacyVault.Domain.Enums;

/// <summary>Kết quả của thao tác ghi trong nhật ký (SEC-07, ADMIN-06 lọc theo kết quả).</summary>
public enum AuditOutcome
{
    /// <summary>Thao tác đã được lưu.</summary>
    Succeeded = 0,
    /// <summary>Bị từ chối vì quyền hoặc trạng thái.</summary>
    Denied = 1,
    /// <summary>Lỗi khi xử lý, không có thay đổi nào được lưu.</summary>
    Failed = 2,
}
