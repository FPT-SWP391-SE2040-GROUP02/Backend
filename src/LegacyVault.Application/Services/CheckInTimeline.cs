using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Tính giờ của một kỳ điểm danh. Hàm thuần: chỉ nhận mốc thời gian, không đọc DB hay đồng hồ hệ thống,
/// nên API và job dùng chung một công thức (TIME-01: mọi mốc là UTC, giờ do server quyết định).
/// </summary>
public static class CheckInTimeline
{
    /// <summary>Hạn điểm danh = lúc bắt đầu kỳ + chu kỳ.</summary>
    public static DateTimeOffset ComputeDueAt(DateTimeOffset startedAt, int cycleDays)
    {
        return startedAt.AddDays(cycleDays);
    }

    /// <summary>Hết thời gian chờ = hạn điểm danh + thời gian chờ.</summary>
    public static DateTimeOffset ComputeGraceEndsAt(DateTimeOffset dueAt, int graceDays)
    {
        return dueAt.AddDays(graceDays);
    }

    /// <summary>
    /// Trạng thái kế hoạch đáng có tại thời điểm now: Active, CheckinOverdue hoặc AwaitingCheck.
    /// API dùng hàm này nên trả đúng theo giờ server kể cả khi job nền chạy chậm. Đúng mốc là đã sang trạng thái sau.
    /// </summary>
    public static PlanStatus EffectiveStatus(DateTimeOffset dueAt, DateTimeOffset graceEndsAt, DateTimeOffset now)
    {
        if (now >= graceEndsAt)
        {
            return PlanStatus.AwaitingCheck;
        }
        if (now >= dueAt)
        {
            return PlanStatus.CheckinOverdue;
        }
        return PlanStatus.Active;
    }

    /// <summary>Thời điểm tới mốc nhắc (DMS-06).</summary>
    public static DateTimeOffset MilestoneAt(CheckInReminderKind kind, DateTimeOffset dueAt, DateTimeOffset graceEndsAt)
    {
        switch (kind)
        {
            case CheckInReminderKind.DueInSevenDays:
                return dueAt.AddDays(-7);
            case CheckInReminderKind.DueInOneDay:
                return dueAt.AddDays(-1);
            case CheckInReminderKind.GraceStarted:
                return dueAt;
            case CheckInReminderKind.GraceTwoDaysLeft:
                return graceEndsAt.AddDays(-2);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mốc nhắc không hợp lệ.");
        }
    }

    /// <summary>Mốc nhắc mới nhất đã tới tại thời điểm now; chưa tới mốc nào → null.</summary>
    public static CheckInReminderKind? LatestReachedMilestone(
        DateTimeOffset dueAt, DateTimeOffset graceEndsAt, DateTimeOffset now)
    {
        CheckInReminderKind[] kinds = Enum.GetValues<CheckInReminderKind>();
        for (int i = kinds.Length - 1; i >= 0; i--)
        {
            if (now >= MilestoneAt(kinds[i], dueAt, graceEndsAt))
            {
                return kinds[i];
            }
        }
        return null;
    }
}
