namespace LegacyVault.Domain.Entities;

/// <summary>
/// Cấu hình lịch điểm danh của một kế hoạch (bảng CheckInSchedules), quan hệ 1–1 với HandoverPlan (SETUP-13).
/// </summary>
/// <remarks>
/// - Dòng này được tạo khi cần (lần đầu Owner chọn cấu hình hoặc lần đầu mở kỳ). Chưa có dòng thì dùng mặc định 30/7.
/// - DMS-01: chu kỳ 30/60/90 ngày, thời gian chờ 7/14/30 ngày. Lựa chọn mới khi đang có kỳ mở được giữ ở
///   PendingCycleDays/PendingGraceDays và chỉ áp dụng từ kỳ sau.
/// - UpdatedAt vừa là concurrency token vừa là ETag của cấu hình.
/// </remarks>
public class CheckInSchedule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid PlanId { get; set; }

    public HandoverPlan Plan { get; set; } = null!;

    /// <summary>Chu kỳ điểm danh đang dùng, tính bằng ngày.</summary>
    public int CycleDays { get; set; } = 30;

    /// <summary>Thời gian chờ sau hạn điểm danh, tính bằng ngày.</summary>
    public int GraceDays { get; set; } = 7;

    /// <summary>Chu kỳ mới, chờ áp dụng khi mở kỳ kế tiếp; null nếu không có.</summary>
    public int? PendingCycleDays { get; set; }

    /// <summary>Thời gian chờ mới, chờ áp dụng khi mở kỳ kế tiếp; null nếu không có.</summary>
    public int? PendingGraceDays { get; set; }

    /// <summary>Lần điểm danh gần nhất; dùng để chống bấm lặp.</summary>
    public DateTimeOffset? LastCheckInAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
