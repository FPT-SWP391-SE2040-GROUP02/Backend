namespace LegacyVault.Domain.Entities;

/// <summary>
/// Một kỳ điểm danh của kế hoạch (bảng CheckInPeriods). Danh sách các kỳ cũng là lịch sử điểm danh.
/// </summary>
/// <remarks>
/// - DueAt và GraceEndsAt được chốt cứng lúc mở kỳ, nên đổi cấu hình sau đó không làm đổi kỳ đang chạy (DMS-01).
/// - Mỗi kế hoạch chỉ có tối đa một kỳ đang mở (ClosedAt = null) — ràng buộc unique có lọc ở DB.
/// - OverdueAt và AwaitingCheckAt chỉ được ghi một lần cho mỗi kỳ (DMS-02). AwaitingCheckAt là dấu để
///   module Executor biết cần giao việc kiểm tra.
/// - RowVersion là concurrency token (giống @Version của Hibernate): điểm danh và job cùng sửa dòng này
///   nên không thể ghi đè nhau.
/// </remarks>
public class CheckInPeriod
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid PlanId { get; set; }

    public HandoverPlan Plan { get; set; } = null!;

    /// <summary>1, 2, 3... duy nhất trong một kế hoạch.</summary>
    public int Sequence { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Hạn điểm danh: quá hạn này kế hoạch sang CHECKIN_OVERDUE.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Hết thời gian chờ: quá mốc này kế hoạch sang AWAITING_CHECK.</summary>
    public DateTimeOffset GraceEndsAt { get; set; }

    /// <summary>Có giá trị nghĩa là Owner đã điểm danh và kỳ này đã đóng.</summary>
    public DateTimeOffset? CheckedInAt { get; set; }

    public DateTimeOffset? OverdueAt { get; set; }

    public DateTimeOffset? AwaitingCheckAt { get; set; }

    /// <summary>Kỳ đóng vì điểm danh hoặc vì dừng lịch; null khi kỳ còn mở.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public List<CheckInReminder> Reminders { get; set; } = new List<CheckInReminder>();
}
