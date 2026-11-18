namespace LegacyVault.Application.Notifications;

/// <summary>
/// Gửi thư mốc lưu. Gửi lỗi thì ném exception; job không đánh dấu mốc đã gửi để vòng sau thử lại.
/// Thư không có tên gói hay tài sản.
/// </summary>
public interface IRetentionNoticeSender
{
    Task SendAsync(RetentionNotice notice, CancellationToken ct);
}
