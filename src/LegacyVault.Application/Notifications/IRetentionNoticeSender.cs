namespace LegacyVault.Application.Notifications;

/// <summary>Thư báo mốc lưu dữ liệu cho người nhận (§7.7 bước 5: bắt đầu, còn 7 ngày, còn 24 giờ).</summary>
public sealed class RetentionNotice
{
    public Guid TurnId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>1 bắt đầu, 2 còn 7 ngày, 3 còn 24 giờ (RetentionRules.Notice*).</summary>
    public int Stage { get; set; }

    public DateTimeOffset DeleteAt { get; set; }
}

/// <summary>
/// Gửi thư mốc lưu (RETENTION-05). Gửi lỗi thì ném exception; job không đánh dấu mốc đã gửi để vòng sau thử lại.
/// OPS-04: thư không có tên gói hay tài sản.
/// </summary>
public interface IRetentionNoticeSender
{
    Task SendAsync(RetentionNotice notice, CancellationToken ct);
}
