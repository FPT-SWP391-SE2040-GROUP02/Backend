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
    public const string ServiceExpired = "SERVICE_EXPIRED";
    public const string RecipientDecisionEnded = "RECIPIENT_DECISION_ENDED";

    public const string Available = "AVAILABLE";
    public const string Passed = "PASSED";
    public const string NotApplicable = "NOT_APPLICABLE";

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

    /// <summary>Thứ tự mốc hiển thị trên màn hình, từ trên xuống: hết hạn dịch vụ, hết hạn quyết định của người nhận.</summary>
    public static List<string> AllMilestones()
    {
        List<string> keys = new List<string>();
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

    /// <summary>Đồng hồ "đã tua" khi thao tác đồng hồ gần nhất trong nhật ký là tua.</summary>
    public static string GetClockState(AuditAction? latestClockAction)
    {
        if (latestClockAction == AuditAction.DevTimeTravel)
        {
            return ClockShifted;
        }
        return ClockRealTime;
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
