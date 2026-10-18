using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Nhật ký một lần yêu cầu tải nội dung (bảng HandoverAccessLogs), phục vụ "lịch sử xem/tải" và điều tra lỗi toàn vẹn.
/// </summary>
/// <remarks>Không ghi nội dung, tên tệp hay khóa (SEC-07).</remarks>
public class HandoverAccessLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid GrantId { get; set; }

    public Guid SnapshotItemId { get; set; }

    public Guid UserId { get; set; }

    public HandoverAccessOutcome Outcome { get; set; }

    public DateTimeOffset At { get; set; }
}
