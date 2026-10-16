using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Luật thuần của việc lưu và xóa dữ liệu sau bàn giao (SRS §7.7, RETENTION-01..09). Không đụng DB.</summary>
public static class RetentionRules
{
    /// <summary>RETENTION-01: 30 ngày lưu trước khi xóa.</summary>
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);

    public const string HoldAwaitingResponse = "AWAITING_RESPONSE";
    public const string HoldClaimInProgress = "CLAIM_IN_PROGRESS";
    public const string HoldGrantActive = "GRANT_ACTIVE";
    public const string HoldObjection = "OBJECTION";
    public const string HoldAdmin = "HOLD";

    public const int NoticeNone = 0;
    public const int NoticeStarted = 1;
    public const int NoticeSevenDays = 2;
    public const int NoticeOneDay = 3;

    /// <summary>
    /// Chỉ kế hoạch đang/đã bàn giao mới áp dụng lưu và xóa. Kế hoạch đã về ACTIVE (Owner còn sống, ALIVE-06) hay
    /// còn nháp thì dữ liệu là của Owner, không bao giờ bị dọn.
    /// </summary>
    public static bool AppliesToPlan(PlanStatus status)
    {
        return status == PlanStatus.HandoverInProgress
            || status == PlanStatus.Closed
            || status == PlanStatus.Blocked;
    }

    /// <summary>Lượt bị hủy vì Owner còn sống không bao giờ dẫn tới xóa dữ liệu.</summary>
    public static bool AppliesToTurn(HandoverTurn turn)
    {
        return turn.Status != HandoverTurnStatus.Cancelled && turn.RetentionStatus != RetentionStatus.Deleted;
    }

    /// <summary>
    /// Lý do đang giữ dữ liệu của lượt tại thời điểm now; null = không còn gì giữ, đồng hồ được chạy.
    /// RETENTION-03: mở link hay đăng nhập không phải lý do giữ; chỉ claim đang xử lý, grant còn hạn hoặc hold đã xác nhận.
    /// </summary>
    public static string? HoldReason(HandoverTurn turn, PlanStatus planStatus, DateTimeOffset now)
    {
        if (planStatus == PlanStatus.Blocked)
        {
            return HoldObjection;
        }
        if (turn.Status == HandoverTurnStatus.Invited && now < turn.ResponseDeadline)
        {
            // §7.7 bước 1: đồng hồ chỉ chạy sau 5 ngày không phản hồi.
            return HoldAwaitingResponse;
        }
        if (turn.Status == HandoverTurnStatus.IdentityInProgress || turn.Status == HandoverTurnStatus.ReadyToAccept)
        {
            // RETENTION-04: claim đang xử lý. TODO(LV-09): giới hạn 30 ngày từ hồ sơ đủ đầu tiên (CLAIM-06).
            return HoldClaimInProgress;
        }
        if (turn.Grant != null && HandoverRules.EffectiveGrantStatus(turn.Grant, now) == HandoverGrantStatus.Active)
        {
            return HoldGrantActive;
        }
        return null;
    }

    /// <summary>
    /// Cập nhật đồng hồ của một lượt theo lý do giữ. Trả true khi lượt đã tới hạn và đủ điều kiện dọn ngay.
    /// Tạm dừng lưu phần còn lại; chạy lại cộng phần còn lại, không bao giờ đặt lại 30 ngày (RETENTION-04).
    /// </summary>
    public static bool Advance(HandoverTurn turn, string? holdReason, DateTimeOffset now)
    {
        if (holdReason != null)
        {
            if (turn.RetentionStatus == RetentionStatus.RetentionActive && turn.RetentionDeadline != null)
            {
                TimeSpan remaining = turn.RetentionDeadline.Value - now;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }
                turn.RetentionStatus = RetentionStatus.DeletionHeld;
                turn.RetentionRemainingSeconds = (long)remaining.TotalSeconds;
                turn.RetentionDeadline = null;
            }
            return false;
        }

        if (turn.RetentionStatus == RetentionStatus.NotStarted)
        {
            turn.RetentionStatus = RetentionStatus.RetentionActive;
            turn.RetentionStartedAt = now;
            turn.RetentionDeadline = now + RetentionWindow;
            return false;
        }
        if (turn.RetentionStatus == RetentionStatus.DeletionHeld)
        {
            long seconds = 0;
            if (turn.RetentionRemainingSeconds != null)
            {
                seconds = turn.RetentionRemainingSeconds.Value;
            }
            turn.RetentionStatus = RetentionStatus.RetentionActive;
            turn.RetentionDeadline = now + TimeSpan.FromSeconds(seconds);
            turn.RetentionRemainingSeconds = null;
        }
        // TIME-02: đúng giây hạn là tới hạn.
        return turn.RetentionStatus == RetentionStatus.RetentionActive
            && turn.RetentionDeadline != null
            && now >= turn.RetentionDeadline.Value;
    }

    /// <summary>Mốc nhắc cần gửi (cao nhất đã tới mà chưa gửi); NoticeNone khi không cần.</summary>
    public static int DueNoticeStage(HandoverTurn turn, DateTimeOffset now)
    {
        if (turn.RetentionStatus != RetentionStatus.RetentionActive || turn.RetentionDeadline == null)
        {
            return NoticeNone;
        }
        TimeSpan left = turn.RetentionDeadline.Value - now;
        int stage = NoticeStarted;
        if (left <= TimeSpan.FromDays(7))
        {
            stage = NoticeSevenDays;
        }
        if (left <= TimeSpan.FromHours(24))
        {
            stage = NoticeOneDay;
        }
        if (stage > turn.RetentionNoticeStage)
        {
            return stage;
        }
        return NoticeNone;
    }
}
