namespace LegacyVault.Domain.Entities;

/// <summary>
/// Cấu hình tham số dịch vụ động (Dynamic System Settings).
/// Thay thế toàn bộ hardcoded limits/quotas/toggles, hỗ trợ chỉnh sửa và bật/tắt realtime từ Admin Dashboard.
/// </summary>
public sealed class SystemSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã định danh tham số (unique), ví dụ Storage.FreeQuotaBytes, Features.MaintenanceMode.</summary>
    public string Key { get; set; } = "";

    /// <summary>Giá trị cấu hình hiện tại (chuỗi tuần tự hóa).</summary>
    public string Value { get; set; } = "";

    /// <summary>Kiểu dữ liệu: STRING, NUMBER, BOOLEAN, JSON.</summary>
    public string DataType { get; set; } = "STRING";

    /// <summary>Phân loại: Storage, Billing, Handover, Features, General.</summary>
    public string Category { get; set; } = "General";

    /// <summary>Mô tả ý nghĩa của tham số cho người quản trị.</summary>
    public string Description { get; set; } = "";

    /// <summary>Cờ bật/tắt áp dụng tham số này.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Thời điểm cập nhật gần nhất.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Email hoặc ID của Admin thực hiện cập nhật.</summary>
    public string? UpdatedBy { get; set; }
}
