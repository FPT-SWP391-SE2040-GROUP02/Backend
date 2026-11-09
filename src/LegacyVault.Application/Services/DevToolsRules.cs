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

    /// <summary>Thứ tự mốc hiển thị trên màn hình, từ trên xuống.</summary>
    public static List<string> AllMilestones()
    {
        List<string> keys = new List<string>();
        keys.Add(CheckInOverdue);
        keys.Add(GraceEnded);
        keys.Add(ServiceExpired);
        keys.Add(RecipientDecisionEnded);
        return keys;
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

    /// <summary>Mốc 2: hết thời gian chờ, kế hoạch sang bước kiểm tra và mở sự cố cho Executor.</summary>
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
