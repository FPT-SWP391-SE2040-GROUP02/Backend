using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật thuần của F2C Owner báo còn sống (SRS §6.3, ALIVE-01..09). Không đụng DB.</summary>
public static class ObjectionRules
{
    /// <summary>ALIVE-03: 7 ngày từ lần báo đầu tiên; báo lại hay thử lại không đặt lại.</summary>
    public static readonly TimeSpan SelfieWindow = TimeSpan.FromDays(7);

    /// <summary>Hạn Verifier kết luận sau khi báo / sau khi yêu cầu bổ sung căn cứ.</summary>
    public static readonly TimeSpan VerifierWindow = TimeSpan.FromDays(7);

    /// <summary>SRS §6.5: Verifier có 48 giờ để kết luận (sau khi được giao, và không sớm hơn hết hạn selfie + 48 giờ).</summary>
    public static readonly TimeSpan VerifierDecisionWindow = TimeSpan.FromHours(48);

    /// <summary>EKYC-20: tối đa 3 lần selfie không khớp trong 24 giờ.</summary>
    public const int MaxSelfieFailures = 3;

    public static readonly TimeSpan SelfieFailureWindow = TimeSpan.FromHours(24);

    public const int MaxReasonLength = 1000;

    /// <summary>Chỉ hồ sơ đã gửi mới báo được: đang xét, cần bổ sung hoặc đã duyệt (đang bàn giao).</summary>
    public static bool CanReport(DeathCaseStatus status)
    {
        return status == DeathCaseStatus.UnderReview
            || status == DeathCaseStatus.SupplementRequired
            || status == DeathCaseStatus.ApprovedForHandover;
    }

    /// <summary>Đúng giây hết hạn là hết hạn (TIME-02).</summary>
    public static bool IsSelfieWindowOpen(DeathCaseObjection objection, DateTimeOffset now)
    {
        return now < objection.SelfieDeadlineAt;
    }

    /// <summary>Số lần thất bại còn tính trong cửa sổ 24 giờ tại thời điểm now.</summary>
    public static int RecentFailures(DeathCaseObjection objection, DateTimeOffset now)
    {
        if (objection.SelfieFailureWindowStartedAt == null)
        {
            return 0;
        }
        if (now - objection.SelfieFailureWindowStartedAt.Value >= SelfieFailureWindow)
        {
            return 0;
        }
        return objection.SelfieFailedCount;
    }

    public static void RecordFailure(DeathCaseObjection objection, DateTimeOffset now)
    {
        if (RecentFailures(objection, now) == 0)
        {
            objection.SelfieFailureWindowStartedAt = now;
            objection.SelfieFailedCount = 0;
        }
        objection.SelfieFailedCount = objection.SelfieFailedCount + 1;
    }

    /// <summary>
    /// ALIVE-05: Verifier chỉ kết luận "còn sống" khi có selfie đạt, hoặc đã hết hạn 7 ngày để Verifier dựa trên hồ sơ
    /// (ALIVE-04). Mật khẩu hay đăng nhập lại không thay được selfie (ALIVE-02).
    /// </summary>
    public static bool HasAliveEvidence(DeathCaseObjection objection, DateTimeOffset now)
    {
        return objection.SelfiePassedAt != null || !IsSelfieWindowOpen(objection, now);
    }

    /// <summary>
    /// ALIVE-05: lời báo đã có selfie khớp mẫu khuôn mặt thì Verifier không được bác phản đối;
    /// chỉ được xác nhận còn sống hoặc ghi chưa đủ căn cứ.
    /// </summary>
    public static bool CanReject(DeathCaseObjection objection)
    {
        return objection.SelfiePassedAt == null;
    }

    /// <summary>
    /// ALIVE-04, ALIVE-08, ASSIGN-04: hạn Verifier kết luận = mốc muộn hơn giữa (start + 48 giờ) và (hết hạn selfie + 48 giờ).
    /// start là lúc báo, lúc giao hoặc giao lại. Hạn selfie 7 ngày của cả lời báo không bao giờ đổi (ALIVE-03).
    /// </summary>
    public static DateTimeOffset ComputeVerifierDueAt(DateTimeOffset start, DateTimeOffset selfieDeadlineAt)
    {
        DateTimeOffset fromStart = start + VerifierDecisionWindow;
        DateTimeOffset fromSelfieDeadline = selfieDeadlineAt + VerifierDecisionWindow;
        if (fromSelfieDeadline > fromStart)
        {
            return fromSelfieDeadline;
        }
        return fromStart;
    }

    /// <summary>TIME-01: đúng lúc hết hạn là quá hạn. Chưa có hạn (chưa có Verifier) thì không tính quá hạn.</summary>
    public static bool IsVerifierOverdue(DeathCaseObjection objection, DateTimeOffset now)
    {
        if (objection.VerifierDueAt == null)
        {
            return false;
        }
        return now >= objection.VerifierDueAt.Value;
    }

    /// <summary>Lượt đã kết thúc không bị hủy lại.</summary>
    public static bool IsTurnFinished(HandoverTurnStatus status)
    {
        return HandoverRules.IsTerminal(status);
    }
}
