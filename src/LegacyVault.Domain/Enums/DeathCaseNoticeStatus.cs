namespace LegacyVault.Domain.Enums;

/// <summary>Trạng thái gửi một thông báo hồ sơ chứng tử cho Owner (outbox, quyết định D3).</summary>
public enum DeathCaseNoticeStatus
{
    /// <summary>Đã ghi nhận cần gửi, chưa gửi được; job sẽ thử lại.</summary>
    Pending = 0,
    /// <summary>Máy chủ SMTP đã nhận thư; 72 giờ phản đối tính từ DeliveredAt (DEATH-08).</summary>
    Delivered = 1,
    /// <summary>Thử đủ 5 lần vẫn lỗi; đồng hồ 72 giờ chưa chạy. TODO: tạo sự cố F4.</summary>
    Failed = 2,
}
