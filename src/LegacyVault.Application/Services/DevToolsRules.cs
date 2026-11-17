using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật của công cụ thử nghiệm: mốc nào tua được với trạng thái hiện tại của kho. Chỉ đọc dữ liệu,
/// không đổi gì. Việc tua chỉ dời hạn về quá khứ; trạng thái mới do job và luật sẵn có của hệ thống quyết định.
/// </summary>
public static class DevToolsRules
{
    public const string CheckInOverdue = "CHECKIN_OVERDUE";
    public const string GraceEnded = "GRACE_ENDED";
    public const string ServiceExpired = "SERVICE_EXPIRED";
    public const string RecipientDecisionEnded = "RECIPIENT_DECISION_ENDED";

    /// <summary>Tiền tố mốc thư nhắc trước hạn thứ n (DUE_REMINDER_1 là thư xa hạn nhất).</summary>
    public const string DueReminderPrefix = "DUE_REMINDER_";

    /// <summary>Tiền tố mốc thư nhắc trễ lần n, từ lần 2 (lần 1 gửi đúng lúc quá hạn nên là mốc CHECKIN_OVERDUE).</summary>
    public const string LateReminderPrefix = "LATE_REMINDER_";

    public const string Available = "AVAILABLE";
    public const string Passed = "PASSED";
    public const string NotApplicable = "NOT_APPLICABLE";

    public const string CheckInOnTime = "ON_TIME";
    public const string CheckInOverdueState = "OVERDUE";
    public const string CheckInNotRunning = "NOT_RUNNING";

    public const string ClockRealTime = "REAL_TIME";
    public const string ClockShifted = "SHIFTED";

    /// <summary>Dời hạn về trước thời điểm hiện tại một khoảng nhỏ để job chắc chắn thấy đã quá hạn.</summary>
    public static readonly TimeSpan PastMargin = TimeSpan.FromMinutes(1);

    /// <summary>Dải an toàn cho ngưỡng so khớp khuôn mặt eKYC khi Admin đổi bằng DevTools.</summary>
    public const double EkycThresholdMin = 0.20;

    public const double EkycThresholdMax = 0.60;

    /// <summary>Ngưỡng nằm trong dải an toàn [EkycThresholdMin; EkycThresholdMax], không phải NaN/Infinity.</summary>
    public static bool IsSafeEkycThreshold(double threshold)
    {
        return !double.IsNaN(threshold)
            && !double.IsInfinity(threshold)
            && threshold >= EkycThresholdMin
            && threshold <= EkycThresholdMax;
    }

    /// <summary>
    /// Sau khi tua hạn về quá khứ, lần điểm danh gần nhất phải lùi theo (hạn trừ đi chu kỳ), nếu không Owner
    /// vừa điểm danh hôm nay sẽ bị khóa "một lần mỗi ngày" dù kế hoạch đã quá hạn. Chỉ lùi, không bao giờ tiến.
    /// </summary>
    public static DateTimeOffset? LastCheckInAfterShiftedDue(DateTimeOffset? lastCheckInAt, DateTimeOffset dueAt, int cycleDays)
    {
        if (lastCheckInAt == null)
        {
            return null;
        }
        DateTimeOffset consistentLastCheckIn = dueAt.AddDays(-cycleDays);
        if (lastCheckInAt.Value > consistentLastCheckIn)
        {
            return consistentLastCheckIn;
        }
        return lastCheckInAt;
    }

    /// <summary>Thứ tự mốc với số thư nhắc tối đa (5 thư trước hạn, 5 thư nhắc trễ); dùng để kiểm tên mốc.</summary>
    public static List<string> AllMilestones()
    {
        return AllMilestones(CheckInRules.MaxDueRemindersLimit, CheckInRules.MaxLateRemindersLimit);
    }

    /// <summary>
    /// Thứ tự mốc hiển thị trên màn hình, từ trên xuống: các thư nhắc trước hạn, quá hạn (thư nhắc trễ lần 1),
    /// các thư nhắc trễ còn lại, hết thời gian chờ, hết hạn dịch vụ, hết hạn quyết định của người nhận.
    /// </summary>
    public static List<string> AllMilestones(int dueReminders, int maxLateReminders)
    {
        List<string> keys = new List<string>();
        for (int i = 1; i <= dueReminders && i <= CheckInRules.MaxDueRemindersLimit; i++)
        {
            keys.Add(DueReminderPrefix + i);
        }
        keys.Add(CheckInOverdue);
        for (int i = 2; i <= maxLateReminders && i <= CheckInRules.MaxLateRemindersLimit; i++)
        {
            keys.Add(LateReminderPrefix + i);
        }
        keys.Add(GraceEnded);
        keys.Add(ServiceExpired);
        keys.Add(RecipientDecisionEnded);
        return keys;
    }

    /// <summary>
    /// Mốc thư nhắc của một tên mốc: DUE_REMINDER_n là thư trước hạn thứ n, CHECKIN_OVERDUE là thư nhắc trễ lần 1,
    /// LATE_REMINDER_n là thư nhắc trễ lần n. Tên mốc khác (hoặc số ngoài khoảng) → null.
    /// </summary>
    public static CheckInReminderKind? ReminderKindOf(string key)
    {
        if (key == CheckInOverdue)
        {
            return CheckInReminderKind.LateReminder1;
        }
        int number = NumberAfter(key, DueReminderPrefix);
        if (number >= 1 && number <= CheckInRules.MaxDueRemindersLimit)
        {
            return CheckInTimeline.DueReminderKind(number);
        }
        number = NumberAfter(key, LateReminderPrefix);
        if (number >= 2 && number <= CheckInRules.MaxLateRemindersLimit)
        {
            return CheckInTimeline.LateReminderKind(number);
        }
        return null;
    }

    private static int NumberAfter(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return 0;
        }
        int number;
        if (int.TryParse(key.Substring(prefix.Length), out number))
        {
            return number;
        }
        return 0;
    }

    /// <summary>
    /// Khoảng cần dời cả kỳ điểm danh về quá khứ để mốc nhắc vừa tới (trước thời điểm hiện tại một chút).
    /// Dời lúc bắt đầu, hạn và hết thời gian chờ cùng một khoảng nên lịch nhắc của kỳ giữ nguyên.
    /// Mốc đã tới thì trả TimeSpan.Zero.
    /// </summary>
    public static TimeSpan ShiftToReach(DateTimeOffset milestoneAt, DateTimeOffset now)
    {
        DateTimeOffset target = now - PastMargin;
        if (milestoneAt <= target)
        {
            return TimeSpan.Zero;
        }
        return milestoneAt - target;
    }

    /// <summary>
    /// Mốc thư nhắc (trước hạn hoặc nhắc trễ) của kỳ đang mở: tua được khi kế hoạch còn ở giai đoạn điểm danh và mốc
    /// chưa tới; đã tới mốc hoặc đã qua giai đoạn điểm danh là đã qua. dueDaysBeforeDue là số ngày của các thư trước hạn.
    /// </summary>
    public static DevMilestoneResponse EvaluateReminder(
        string key, PlanStatus status, CheckInPeriod? openPeriod, DateTimeOffset now, IReadOnlyList<int> dueDaysBeforeDue)
    {
        CheckInReminderKind kind = ReminderKindOf(key)!.Value;
        int duePosition = CheckInTimeline.DueReminderPosition(kind);
        DevMilestoneResponse result;
        if (status == PlanStatus.AwaitingCheck || IsBeyondCheckIn(status))
        {
            result = Build(key, Passed, string.Empty);
        }
        else if ((status != PlanStatus.Active && status != PlanStatus.CheckinOverdue) || openPeriod == null)
        {
            result = Build(key, NotApplicable, "Kế hoạch chưa kích hoạt nên chưa có kỳ điểm danh.");
        }
        else if (duePosition > 0 && status != PlanStatus.Active)
        {
            result = Build(key, Passed, string.Empty);
        }
        else
        {
            DateTimeOffset at = CheckInTimeline.MilestoneAt(
                kind, openPeriod.DueAt, openPeriod.LateReminderGapDays, dueDaysBeforeDue);
            if (at <= now)
            {
                result = Build(key, Passed, string.Empty);
            }
            else
            {
                result = Build(key, Available, string.Empty);
            }
        }
        if (duePosition > 0 && duePosition <= dueDaysBeforeDue.Count)
        {
            result.DaysBeforeDue = dueDaysBeforeDue[duePosition - 1];
        }
        return result;
    }

    public static bool IsKnownMilestone(string key)
    {
        foreach (string known in AllMilestones())
        {
            if (known == key)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Kế hoạch đã đi qua giai đoạn điểm danh (đang chứng tử, bàn giao, bị chặn hoặc đã đóng).</summary>
    public static bool IsBeyondCheckIn(PlanStatus status)
    {
        if (status == PlanStatus.DeathCaseOpen
            || status == PlanStatus.HandoverInProgress
            || status == PlanStatus.Blocked
            || status == PlanStatus.Closed)
        {
            return true;
        }
        return false;
    }

    /// <summary>Trạng thái điểm danh để hiện nhãn "Điểm danh: ...".</summary>
    public static string GetCheckInState(PlanStatus status, CheckInPeriod? openPeriod, DateTimeOffset now)
    {
        if (!PlanRules.IsRunning(status) || openPeriod == null)
        {
            return CheckInNotRunning;
        }
        if (status != PlanStatus.Active || openPeriod.DueAt <= now)
        {
            return CheckInOverdueState;
        }
        return CheckInOnTime;
    }

    /// <summary>Đồng hồ "đã tua" khi thao tác đồng hồ gần nhất trong nhật ký là tua.</summary>
    public static string GetClockState(AuditAction? latestClockAction)
    {
        if (latestClockAction == AuditAction.DevTimeTravel)
        {
            return ClockShifted;
        }
        return ClockRealTime;
    }

    /// <summary>Mốc 1: kỳ điểm danh đang mở chuyển sang quá hạn, vẫn còn thời gian chờ.</summary>
    public static DevMilestoneResponse EvaluateCheckInOverdue(PlanStatus status, CheckInPeriod? openPeriod, DateTimeOffset now)
    {
        if (status == PlanStatus.CheckinOverdue || status == PlanStatus.AwaitingCheck || IsBeyondCheckIn(status))
        {
            return Build(CheckInOverdue, Passed, string.Empty);
        }
        if (status != PlanStatus.Active || openPeriod == null)
        {
            return Build(CheckInOverdue, NotApplicable, "Kế hoạch chưa kích hoạt nên chưa có kỳ điểm danh.");
        }
        if (openPeriod.DueAt <= now)
        {
            return Build(CheckInOverdue, Passed, string.Empty);
        }
        return Build(CheckInOverdue, Available, string.Empty);
    }

    /// <summary>Mốc 2: hết thời gian chờ, kế hoạch sang bước kiểm tra.</summary>
    public static DevMilestoneResponse EvaluateGraceEnded(PlanStatus status, CheckInPeriod? openPeriod, DateTimeOffset now)
    {
        if (status == PlanStatus.AwaitingCheck || IsBeyondCheckIn(status))
        {
            return Build(GraceEnded, Passed, string.Empty);
        }
        if ((status != PlanStatus.Active && status != PlanStatus.CheckinOverdue) || openPeriod == null)
        {
            return Build(GraceEnded, NotApplicable, "Kế hoạch chưa kích hoạt nên chưa có kỳ điểm danh.");
        }
        if (openPeriod.GraceEndsAt <= now)
        {
            return Build(GraceEnded, Passed, string.Empty);
        }
        return Build(GraceEnded, Available, string.Empty);
    }

    /// <summary>
    /// Mốc hết hạn dịch vụ (OPLAN-01): kho đang trả phí thì tua được; đã ở ân hạn hoặc rà soát là đã qua;
    /// gói Free không có hạn dịch vụ.
    /// </summary>
    public static DevMilestoneResponse EvaluateServiceExpired(VaultEntitlement? entitlement)
    {
        if (entitlement == null || entitlement.Status == EntitlementStatus.Free)
        {
            return Build(ServiceExpired, NotApplicable, "Kho đang ở gói Free nên không có hạn dịch vụ.");
        }
        if (entitlement.Status == EntitlementStatus.GraceReadOnly || entitlement.Status == EntitlementStatus.ExpiredReview)
        {
            return Build(ServiceExpired, Passed, string.Empty);
        }
        if (entitlement.ServiceExpiresAt == null)
        {
            return Build(ServiceExpired, NotApplicable, "Kho chưa có hạn dịch vụ.");
        }
        return Build(ServiceExpired, Available, string.Empty);
    }

    /// <summary>
    /// Mốc hết 7 ngày quyết định của người nhận: tua được khi còn lượt đã xác nhận danh tính (READY_TO_ACCEPT) và chưa
    /// tới hạn quyết định; đã có lượt hết hạn quyết định mà không còn lượt nào chờ là đã qua.
    /// </summary>
    public static DevMilestoneResponse EvaluateRecipientDecision(List<HandoverTurn> turns, DateTimeOffset now)
    {
        bool anyExpired = false;
        foreach (HandoverTurn turn in turns)
        {
            if (turn.Status == HandoverTurnStatus.ReadyToAccept && turn.DecisionDeadline != null)
            {
                if (turn.DecisionDeadline.Value > now)
                {
                    return Build(RecipientDecisionEnded, Available, string.Empty);
                }
                anyExpired = true;
            }
            if (turn.Status == HandoverTurnStatus.DecisionExpired)
            {
                anyExpired = true;
            }
        }
        if (anyExpired)
        {
            return Build(RecipientDecisionEnded, Passed, string.Empty);
        }
        return Build(RecipientDecisionEnded, NotApplicable,
            "Chưa có người nhận nào đã xác nhận danh tính và đang trong 7 ngày quyết định.");
    }

    private static DevMilestoneResponse Build(string key, string state, string note)
    {
        DevMilestoneResponse milestone = new DevMilestoneResponse();
        milestone.Key = key;
        milestone.State = state;
        milestone.Note = note;
        return milestone;
    }
}
