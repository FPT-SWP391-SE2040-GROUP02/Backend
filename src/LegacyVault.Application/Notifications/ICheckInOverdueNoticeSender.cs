namespace LegacyVault.Application.Notifications;

/// <summary>Dữ liệu thư báo Executor rằng Owner đã quá hạn điểm danh (DMS-05).</summary>
/// <remarks>OPS-04, SEC-07: không có tên hay thông tin Owner; chỉ mã kho (để tạo link) và hạn điểm danh.</remarks>
public sealed class CheckInOverdueNotice
{
    public string ToEmail { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Kho của kỳ điểm danh; link trong thư mở đúng kho trên trang Executor.</summary>
    public Guid VaultId { get; set; }

    public DateTimeOffset DueAt { get; set; }
}

/// <summary>
/// Gửi thư cho Executor khi kế hoạch vừa sang AWAITING_CHECK (DMS-05). Cài đặt ở Infrastructure (email).
/// Gửi lỗi thì ném exception; CheckInJob chỉ ghi log, không hoàn tác việc đổi trạng thái.
/// </summary>
public interface ICheckInOverdueNoticeSender
{
    Task SendAsync(CheckInOverdueNotice notice, CancellationToken ct);
}
