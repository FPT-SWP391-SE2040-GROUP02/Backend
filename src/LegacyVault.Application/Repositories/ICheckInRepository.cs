using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập các bảng CheckInSchedules, CheckInPeriods, CheckInReminders.</summary>
/// <remarks>
/// Repository chỉ đánh dấu Add/sửa entity; service gọi IUnitOfWork.SaveChangesAsync một lần ở cuối use case.
/// </remarks>
public interface ICheckInRepository
{
    /// <summary>Cấu hình điểm danh của kế hoạch (có theo dõi thay đổi). Chưa có → null.</summary>
    Task<CheckInSchedule?> FindScheduleByPlanIdAsync(Guid planId, CancellationToken ct);

    /// <summary>Kỳ đang mở (ClosedAt = null) của kế hoạch, có theo dõi thay đổi. Không có → null.</summary>
    Task<CheckInPeriod?> FindOpenPeriodByPlanIdAsync(Guid planId, CancellationToken ct);

    /// <summary>Số thứ tự lớn nhất của các kỳ đã có; chưa có kỳ nào → 0.</summary>
    Task<int> GetMaxSequenceAsync(Guid planId, CancellationToken ct);

    /// <summary>Một trang lịch sử các kỳ, kỳ mới nhất (Sequence lớn nhất) trước. pageIndex bắt đầu từ 1.</summary>
    Task<(List<CheckInPeriod> Items, int TotalCount)> FindPeriodPageByPlanIdAsync(
        Guid planId, int pageIndex, int pageSize, CancellationToken ct);

    /// <summary>
    /// Id các kỳ đang mở của kế hoạch ACTIVE/CHECKIN_OVERDUE mà đã tới lúc cần xử lý:
    /// DueAt không quá 7 ngày nữa (mốc nhắc sớm nhất) tính từ now.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindOpenPeriodIdsForJobAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Một kỳ kèm Plan, Plan.Vault, Plan.Vault.Owner và Reminders, có theo dõi thay đổi. Không có → null.
    /// </summary>
    Task<CheckInPeriod?> FindPeriodForJobAsync(Guid periodId, CancellationToken ct);

    void AddSchedule(CheckInSchedule schedule);

    void AddPeriod(CheckInPeriod period);

    void AddReminder(CheckInReminder reminder);
}
