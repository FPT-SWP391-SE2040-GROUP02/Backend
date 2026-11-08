using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật trạng thái của tính năng điểm danh (SRS §6.1, DMS-01).</summary>
public static class CheckInRules
{
    /// <summary>Chu kỳ điểm danh mặc định (ngày).</summary>
    public static int DefaultCycleDays => RuntimeSettings.Int("CheckIn.DefaultCycleDays", 30);

    /// <summary>Số thư nhắc trễ tối đa mà Admin được đặt (khớp các mốc LateReminder1 đến LateReminder5).</summary>
    public const int MaxLateRemindersLimit = 5;

    /// <summary>Khoảng cách giữa hai thư nhắc trễ (ngày), Admin đặt, từ 1 đến 30.</summary>
    public static int LateReminderGapDays => Math.Clamp(RuntimeSettings.Int("CheckIn.LateReminderGapDays", 3), 1, 30);

    /// <summary>Số thư nhắc trễ gửi cho chủ kho trước khi báo Người thực thi, Admin đặt, từ 1 đến 5.</summary>
    public static int MaxLateReminders => Math.Clamp(RuntimeSettings.Int("CheckIn.MaxLateReminders", 3), 1, MaxLateRemindersLimit);

    /// <summary>Số lần Người thực thi được điểm danh thay liên tiếp (về 0 khi chủ kho tự điểm danh), Admin đặt.</summary>
    public static int ExecutorProxyLimit => ProxyCheckInRules.Limit();

    /// <summary>
    /// Mở một kỳ mới: chốt hạn điểm danh, số thư nhắc trễ, khoảng cách và mốc báo Người thực thi theo cấu hình hiện tại.
    /// Đổi cấu hình sau đó chỉ áp dụng từ kỳ sau (DMS-01).
    /// </summary>
    public static void StartPeriodTimeline(CheckInPeriod period, DateTimeOffset startedAt, int cycleDays)
    {
        period.StartedAt = startedAt;
        period.DueAt = CheckInTimeline.ComputeDueAt(startedAt, cycleDays);
        period.LateReminderGapDays = LateReminderGapDays;
        period.MaxLateReminders = MaxLateReminders;
        period.GraceEndsAt = CheckInTimeline.ComputeNotifyExecutorAt(
            period.DueAt, period.MaxLateReminders, period.LateReminderGapDays);
    }

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Mỗi ngày lịch Việt Nam chỉ ghi nhận một lần, dùng giờ server cho cả web và đồng hồ.</summary>
    public static bool HasCheckedInToday(DateTimeOffset? lastCheckInAt, DateTimeOffset now)
    {
        if (lastCheckInAt == null)
        {
            return false;
        }
        if (TimeUnits.IsDemo)
        {
            // Demo: "một ngày" chỉ còn 1 giây nên điểm danh được lại gần như ngay.
            return now - lastCheckInAt.Value < TimeUnits.Days(1);
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
        if (TimeUnits.IsDemo)
        {
            return now - openPeriod.StartedAt < TimeUnits.Days(1);
        }
        DateTime startedDate = openPeriod.StartedAt.ToOffset(VietnamOffset).Date;
        DateTime today = now.ToOffset(VietnamOffset).Date;
        return startedDate >= today;
    }

    private static readonly int[] AllowedCycleDays = { 30, 60, 90 };

    public static bool IsAllowedCycleDays(int days)
    {
        return days == DefaultCycleDays || Array.IndexOf(AllowedCycleDays, days) >= 0;
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
        if (changed)
        {
            schedule.UpdatedAt = now;
        }
    }

}
