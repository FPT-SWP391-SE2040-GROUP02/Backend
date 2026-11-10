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
        return startedAt + TimeUnits.Days(cycleDays);
    }

    /// <summary>
    /// Mốc báo Người thực thi = hạn điểm danh + số thư nhắc trễ × khoảng cách giữa các thư.
    /// Ví dụ hạn ngày 30, 3 thư cách nhau 3 ngày: thư gửi ngày 30, 33, 36 và báo Người thực thi ngày 39.
    /// </summary>
    public static DateTimeOffset ComputeNotifyExecutorAt(DateTimeOffset dueAt, int maxLateReminders, int gapDays)
    {
        return dueAt + TimeUnits.Days(maxLateReminders * gapDays);
    }

    /// <summary>
    /// Trạng thái kế hoạch đáng có tại thời điểm now: Active, CheckinOverdue hoặc AwaitingCheck.
    /// API dùng hàm này nên trả đúng theo giờ server kể cả khi job nền chạy chậm. Đúng mốc là đã sang trạng thái sau.
    /// </summary>
    public static PlanStatus EffectiveStatus(DateTimeOffset dueAt, DateTimeOffset notifyExecutorAt, DateTimeOffset now)
    {
        if (now >= notifyExecutorAt)
        {
            return PlanStatus.AwaitingCheck;
        }
        if (now >= dueAt)
        {
            return PlanStatus.CheckinOverdue;
        }
        return PlanStatus.Active;
    }

    /// <summary>Số thứ tự thư nhắc trễ (1 đến 5) của một mốc; mốc khác → 0.</summary>
    public static int LateReminderIndex(CheckInReminderKind kind)
    {
        if (kind >= CheckInReminderKind.LateReminder1 && kind <= CheckInReminderKind.LateReminder5)
        {
            return (int)kind - (int)CheckInReminderKind.LateReminder1 + 1;
        }
        return 0;
    }

    /// <summary>Mốc của thư nhắc trễ thứ index (1 đến 5).</summary>
    public static CheckInReminderKind LateReminderKind(int index)
    {
        if (index < 1 || index > CheckInRules.MaxLateRemindersLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Thư nhắc trễ chỉ có từ lần 1 đến lần 5.");
        }
        return (CheckInReminderKind)((int)CheckInReminderKind.LateReminder1 + index - 1);
    }

    /// <summary>Các mốc nhắc còn dùng của một kỳ, theo thứ tự thời gian: hai mốc trước hạn rồi các thư nhắc trễ.</summary>
    public static IReadOnlyList<CheckInReminderKind> ActiveKinds(int maxLateReminders)
    {
        List<CheckInReminderKind> kinds = new List<CheckInReminderKind>();
        kinds.Add(CheckInReminderKind.DueInSevenDays);
        kinds.Add(CheckInReminderKind.DueInOneDay);
        for (int i = 1; i <= maxLateReminders && i <= CheckInRules.MaxLateRemindersLimit; i++)
        {
            kinds.Add(LateReminderKind(i));
        }
        return kinds;
    }

    /// <summary>
    /// Thời điểm tới mốc nhắc (DMS-06) với hai mốc trước hạn ở mức mặc định (7 ngày và 1 ngày).
    /// Thư nhắc trễ lần n gửi lúc hạn + (n - 1) × khoảng cách.
    /// </summary>
    public static DateTimeOffset MilestoneAt(CheckInReminderKind kind, DateTimeOffset dueAt, int gapDays)
    {
        return MilestoneAt(kind, dueAt, gapDays, CheckInRules.DefaultFirstReminderDays, CheckInRules.DefaultSecondReminderDays);
    }

    /// <summary>
    /// Như bản trên nhưng hai thư trước hạn gửi trước hạn firstDaysBeforeDue và secondDaysBeforeDue ngày
    /// (Admin đặt, xem CheckInRules). Tên mốc DueInSevenDays và DueInOneDay chỉ là tên cũ của hai mốc này.
    /// </summary>
    public static DateTimeOffset MilestoneAt(
        CheckInReminderKind kind, DateTimeOffset dueAt, int gapDays, int firstDaysBeforeDue, int secondDaysBeforeDue)
    {
        switch (kind)
        {
            case CheckInReminderKind.DueInSevenDays:
                return dueAt - TimeUnits.Days(firstDaysBeforeDue);
            case CheckInReminderKind.DueInOneDay:
                return dueAt - TimeUnits.Days(secondDaysBeforeDue);
            default:
                int late = LateReminderIndex(kind);
                if (late == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mốc nhắc không còn dùng.");
                }
                return dueAt.AddDays((double)(late - 1) * gapDays);
        }
    }

    /// <summary>Mốc nhắc mới nhất đã tới tại thời điểm now; chưa tới mốc nào → null.</summary>
    public static CheckInReminderKind? LatestReachedMilestone(
        DateTimeOffset dueAt, int maxLateReminders, int gapDays, DateTimeOffset now)
    {
        return LatestReachedMilestone(
            dueAt, maxLateReminders, gapDays, now, CheckInRules.DefaultFirstReminderDays, CheckInRules.DefaultSecondReminderDays);
    }

    /// <summary>Như bản trên nhưng với số ngày của hai thư trước hạn do Admin đặt.</summary>
    public static CheckInReminderKind? LatestReachedMilestone(
        DateTimeOffset dueAt, int maxLateReminders, int gapDays, DateTimeOffset now,
        int firstDaysBeforeDue, int secondDaysBeforeDue)
    {
        IReadOnlyList<CheckInReminderKind> kinds = ActiveKinds(maxLateReminders);
        for (int i = kinds.Count - 1; i >= 0; i--)
        {
            if (now >= MilestoneAt(kinds[i], dueAt, gapDays, firstDaysBeforeDue, secondDaysBeforeDue))
            {
                return kinds[i];
            }
        }
        return null;
    }
}
