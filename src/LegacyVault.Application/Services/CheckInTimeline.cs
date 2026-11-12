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

    /// <summary>Thứ tự (1 đến 5) của một mốc nhắc trước hạn, tính từ thư xa hạn nhất; mốc khác → 0.</summary>
    public static int DueReminderPosition(CheckInReminderKind kind)
    {
        switch (kind)
        {
            case CheckInReminderKind.DueInSevenDays:
                return 1;
            case CheckInReminderKind.DueInOneDay:
                return 2;
            case CheckInReminderKind.DueReminder3:
                return 3;
            case CheckInReminderKind.DueReminder4:
                return 4;
            case CheckInReminderKind.DueReminder5:
                return 5;
            default:
                return 0;
        }
    }

    /// <summary>Mốc của thư nhắc trước hạn thứ position (1 đến 5).</summary>
    public static CheckInReminderKind DueReminderKind(int position)
    {
        switch (position)
        {
            case 1:
                return CheckInReminderKind.DueInSevenDays;
            case 2:
                return CheckInReminderKind.DueInOneDay;
            case 3:
                return CheckInReminderKind.DueReminder3;
            case 4:
                return CheckInReminderKind.DueReminder4;
            case 5:
                return CheckInReminderKind.DueReminder5;
            default:
                throw new ArgumentOutOfRangeException(nameof(position), position, "Thư nhắc trước hạn chỉ có từ lần 1 đến lần 5.");
        }
    }

    /// <summary>Các mốc nhắc còn dùng của một kỳ với hai thư trước hạn, theo thứ tự thời gian.</summary>
    public static IReadOnlyList<CheckInReminderKind> ActiveKinds(int maxLateReminders)
    {
        return ActiveKinds(CheckInRules.DefaultDueReminderCount, maxLateReminders);
    }

    /// <summary>
    /// Các mốc nhắc còn dùng của một kỳ, theo thứ tự thời gian: dueReminders mốc trước hạn rồi các thư nhắc trễ.
    /// Dùng thứ tự của danh sách này, không dùng giá trị số của enum, để biết mốc nào đến trước.
    /// </summary>
    public static IReadOnlyList<CheckInReminderKind> ActiveKinds(int dueReminders, int maxLateReminders)
    {
        List<CheckInReminderKind> kinds = new List<CheckInReminderKind>();
        for (int i = 1; i <= dueReminders && i <= CheckInRules.MaxDueRemindersLimit; i++)
        {
            kinds.Add(DueReminderKind(i));
        }
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
        return MilestoneAt(kind, dueAt, gapDays, new int[] { firstDaysBeforeDue, secondDaysBeforeDue });
    }

    /// <summary>
    /// Như bản trên nhưng với số ngày của mọi thư trước hạn (từ xa hạn nhất đến gần hạn nhất, xem CheckInRules.DueReminderDays).
    /// </summary>
    public static DateTimeOffset MilestoneAt(
        CheckInReminderKind kind, DateTimeOffset dueAt, int gapDays, IReadOnlyList<int> dueDaysBeforeDue)
    {
        int position = DueReminderPosition(kind);
        if (position > 0)
        {
            if (position > dueDaysBeforeDue.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mốc nhắc trước hạn này chưa được đặt số ngày.");
            }
            return dueAt - TimeUnits.Days(dueDaysBeforeDue[position - 1]);
        }
        int late = LateReminderIndex(kind);
        if (late == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mốc nhắc không còn dùng.");
        }
        return dueAt.AddDays((double)(late - 1) * gapDays);
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
        return LatestReachedMilestone(
            dueAt, maxLateReminders, gapDays, now, new int[] { firstDaysBeforeDue, secondDaysBeforeDue });
    }

    /// <summary>Như bản trên nhưng với số ngày của mọi thư trước hạn; số thư trước hạn là số phần tử của danh sách.</summary>
    public static CheckInReminderKind? LatestReachedMilestone(
        DateTimeOffset dueAt, int maxLateReminders, int gapDays, DateTimeOffset now, IReadOnlyList<int> dueDaysBeforeDue)
    {
        IReadOnlyList<CheckInReminderKind> kinds = ActiveKinds(dueDaysBeforeDue.Count, maxLateReminders);
        for (int i = kinds.Count - 1; i >= 0; i--)
        {
            if (now >= MilestoneAt(kinds[i], dueAt, gapDays, dueDaysBeforeDue))
            {
                return kinds[i];
            }
        }
        return null;
    }
}
