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

    /// <summary>Điểm danh lặp trong khoảng này kể từ lần trước chỉ trả trạng thái hiện tại, không mở kỳ mới.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

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
