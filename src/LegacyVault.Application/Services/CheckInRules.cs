using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật trạng thái của tính năng điểm danh (SRS §6.1, DMS-01).</summary>
public static class CheckInRules
{
    /// <summary>Chu kỳ điểm danh mặc định (ngày).</summary>
    public const int DefaultCycleDays = 30;

    /// <summary>Thời gian chờ mặc định sau hạn điểm danh (ngày).</summary>
    public const int DefaultGraceDays = 7;

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Mỗi ngày lịch Việt Nam chỉ ghi nhận một lần, dùng giờ server cho cả web và đồng hồ.</summary>
    public static bool HasCheckedInToday(DateTimeOffset? lastCheckInAt, DateTimeOffset now)
    {
        if (lastCheckInAt == null)
        {
            return false;
        }
        DateTime lastDate = lastCheckInAt.Value.ToOffset(VietnamOffset).Date;
        DateTime today = now.ToOffset(VietnamOffset).Date;
        // Giữ khóa cả khi dữ liệu nằm ở tương lai (ví dụ kho vừa được tua giờ demo).
        return lastDate >= today;
    }

    /// <summary>
    /// Kỳ đang mở do Executor điểm danh thay và bắt đầu trong ngày Việt Nam hôm nay. Chủ kho coi như đã được điểm danh
    /// hôm nay: không bấm lại được và màn hình báo "Executor đã điểm danh hộ".
    /// </summary>
    public static bool StartedByExecutorToday(CheckInPeriod? openPeriod, DateTimeOffset now)
    {
        if (openPeriod == null || !openPeriod.StartedByExecutor)
        {
            return false;
        }
        DateTime startedDate = openPeriod.StartedAt.ToOffset(VietnamOffset).Date;
        DateTime today = now.ToOffset(VietnamOffset).Date;
        return startedDate >= today;
    }

    private static readonly int[] AllowedCycleDays = { 30, 60, 90 };
    private static readonly int[] AllowedGraceDays = { 7, 14, 30 };

    public static bool IsAllowedCycleDays(int days)
    {
        return Array.IndexOf(AllowedCycleDays, days) >= 0;
    }

    public static bool IsAllowedGraceDays(int days)
    {
        return Array.IndexOf(AllowedGraceDays, days) >= 0;
    }

    /// <summary>Kế hoạch đang chạy lịch điểm danh: Owner được điểm danh (SRS §11.2).</summary>
    public static bool IsRunning(PlanStatus status)
    {
        return PlanRules.IsRunning(status);
    }

    /// <summary>Lịch điểm danh không còn chạy vì kế hoạch đã sang giai đoạn chứng tử/bàn giao hoặc bị chặn.</summary>
    public static bool IsPaused(PlanStatus status)
    {
        if (status == PlanStatus.DeathCaseOpen
            || status == PlanStatus.HandoverInProgress
            || status == PlanStatus.Closed
            || status == PlanStatus.Blocked)
        {
            return true;
        }
        return false;
    }

    /// <summary>Kế hoạch đã ghim snapshot/đang bàn giao/đã đóng: không đổi cấu hình điểm danh (SETUP-05).</summary>
    public static bool IsSettingsLocked(PlanStatus status)
    {
        if (status == PlanStatus.DeathCaseOpen
            || status == PlanStatus.HandoverInProgress
            || status == PlanStatus.Closed)
        {
            return true;
        }
        return false;
    }
    /// <summary>DMS-01: lựa chọn chờ áp dụng được dùng khi mở kỳ mới, rồi xóa.</summary>
    public static void ApplyPendingSettings(CheckInSchedule schedule, DateTimeOffset now)
    {
        bool changed = false;
        if (schedule.PendingCycleDays != null)
        {
            schedule.CycleDays = schedule.PendingCycleDays.Value;
            schedule.PendingCycleDays = null;
            changed = true;
        }
        if (schedule.PendingGraceDays != null)
        {
            schedule.GraceDays = schedule.PendingGraceDays.Value;
            schedule.PendingGraceDays = null;
            changed = true;
        }
        if (changed)
        {
            schedule.UpdatedAt = now;
        }
    }

}
