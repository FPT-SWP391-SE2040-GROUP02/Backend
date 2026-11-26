namespace LegacyVault.Application.Notifications;

/// <summary>
/// Dữ liệu thư báo một người nhận rằng họ đã được chọn, gửi khi Chủ tài sản kích hoạt kế hoạch.
/// Chỉ có tên và thông tin liên hệ của Chủ tài sản và Người thực thi; không có tên gói hay tài sản.
/// </summary>
public sealed class PlanActivatedNotice
{
    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Link riêng (chưa băm) để người nhận gửi yêu cầu nhận tài sản; rỗng thì nút dẫn về trang giới thiệu.</summary>
    public string RequestToken { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    public string OwnerEmail { get; set; } = string.Empty;

    public string? OwnerPhone { get; set; }

    /// <summary>Null khi kho không có Người thực thi đã chấp nhận (không xảy ra khi kích hoạt đúng điều kiện).</summary>
    public string? ExecutorName { get; set; }

    public string? ExecutorEmail { get; set; }

    public string? ExecutorPhone { get; set; }
}
