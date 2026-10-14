using LegacyVault.Domain.Enums;

namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một lời nhắc điểm danh của một kỳ (bảng CheckInReminders).
/// </summary>
/// <remarks>
/// Unique (PeriodId, Kind): mỗi mốc của mỗi kỳ chỉ có một dòng, nên không thể gửi trùng dù job chạy lặp
/// hoặc chạy trên hai instance (TIME-02).
/// </remarks>
public class CheckInReminder
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid PeriodId { get; set; }

    public CheckInPeriod Period { get; set; } = null!;

    public CheckInReminderKind Kind { get; set; }

    public CheckInReminderStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
