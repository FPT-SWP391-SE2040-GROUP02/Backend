namespace LegacyVault.Application.Repositories;

/// <summary>Thông tin một kỳ điểm danh đang mở, đủ để tính các mốc nhắc bằng CheckInTimeline.</summary>
public sealed class DemoOpenPeriod
{
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset GraceEndsAt { get; set; }
    public int MaxLateReminders { get; set; }
    public int LateReminderGapDays { get; set; }
}

/// <summary>Một mốc hạn sắp tới ngoài điểm danh, kèm loại mốc để hiện trên dòng thời gian.</summary>
public sealed class DemoDeadline
{
    public DateTimeOffset At { get; set; }
    public string Kind { get; set; } = string.Empty;
}

/// <summary>
/// Truy vấn riêng cho công cụ tua thời gian (DEMO) và nơi lưu độ lệch đồng hồ.
/// Độ lệch lưu trong bảng SystemSettings (không cần bảng mới) để khởi động lại máy chủ không mất.
/// </summary>
public interface IDemoClockRepository
{
    /// <summary>Độ lệch đã lưu (giây); null khi chưa từng lưu.</summary>
    Task<long?> FindOffsetSecondsAsync(CancellationToken ct);

    /// <summary>Lưu độ lệch (giây). Tự thêm dòng cấu hình nếu chưa có và tự lưu vào DB.</summary>
    Task SaveOffsetSecondsAsync(long offsetSeconds, DateTimeOffset now, CancellationToken ct);

    /// <summary>Các kỳ điểm danh đang mở của kế hoạch Active hoặc CheckinOverdue.</summary>
    Task<List<DemoOpenPeriod>> ListOpenCheckInPeriodsAsync(CancellationToken ct);

    /// <summary>
    /// Các mốc hạn của lời mời Người thực thi, hạn dịch vụ, hạn phản hồi/quyết định/tải của bàn giao và sự cố,
    /// nằm trong khoảng (after, until]. remindAfter là khoảng từ lúc mời tới lúc nhắc, incidentEscalateAfter là
    /// khoảng từ lúc tạo sự cố tới lúc báo lên.
    /// </summary>
    Task<List<DemoDeadline>> ListUpcomingDeadlinesAsync(
        DateTimeOffset after, DateTimeOffset until, TimeSpan remindAfter, TimeSpan incidentEscalateAfter,
        CancellationToken ct);

    /// <summary>Số thư nhắc điểm danh đã gửi từ trước tới nay (để đếm số thư gửi thêm ở mỗi bước).</summary>
    Task<int> CountSentCheckInRemindersAsync(CancellationToken ct);
}
