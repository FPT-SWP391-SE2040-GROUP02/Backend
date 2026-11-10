using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>Hằng số và luật trạng thái thuần của giai đoạn nhận tài sản (SRS §6.3, TIME-02).</summary>
public static class HandoverRules
{
    /// <summary>Hạn phản hồi lời mời, tính từ lúc mời (INVITE-02).</summary>
    public static TimeSpan ResponseWindow => RuntimeSettings.Days("Handover.ResponseWindowDays", 5);

    /// <summary>Hạn quyết định đồng ý/từ chối, tính từ lúc sẵn sàng nhận (ACCEPT-02).</summary>
    public static TimeSpan DecisionWindow => RuntimeSettings.Days("Handover.DecisionWindowDays", 7);

    /// <summary>Thời hạn quyền xem và tải, tính từ lúc đồng ý (DOWNLOAD-01).</summary>
    public static TimeSpan GrantWindow => RuntimeSettings.Days("Handover.GrantWindowDays", 30);

    /// <summary>Lượt đã kết thúc, không đổi trạng thái nữa.</summary>
    public static bool IsTerminal(HandoverTurnStatus status)
    {
        switch (status)
        {
            case HandoverTurnStatus.HandoverCommitted:
            case HandoverTurnStatus.Declined:
            case HandoverTurnStatus.DecisionExpired:
            case HandoverTurnStatus.Cancelled:
            case HandoverTurnStatus.Lapsed:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lượt còn cho phép người nhận "nhận lượt" (quá hạn phản hồi vẫn nhận được, CLAIM-02).</summary>
    public static bool CanClaim(HandoverTurnStatus status)
    {
        return status == HandoverTurnStatus.Invited || status == HandoverTurnStatus.InitialResponseExpired;
    }

    /// <summary>Đúng giây hạn là đã quá hạn: now &gt;= deadline.</summary>
    public static bool IsExpired(DateTimeOffset deadline, DateTimeOffset now)
    {
        return now >= deadline;
    }

    /// <summary>Trạng thái grant tại thời điểm now, kể cả khi job hết hạn chưa chạy.</summary>
    public static HandoverGrantStatus EffectiveGrantStatus(HandoverGrant grant, DateTimeOffset now)
    {
        if (grant.Status == HandoverGrantStatus.Active && IsExpired(grant.ExpiresAt, now))
        {
            return HandoverGrantStatus.Expired;
        }

        return grant.Status;
    }

    /// <summary>Chuyển lượt sang "sẵn sàng nhận" và đặt hạn quyết định.</summary>
    public static void MarkReadyToAccept(HandoverTurn turn, DateTimeOffset now)
    {
        turn.Status = HandoverTurnStatus.ReadyToAccept;
        turn.ReadyAt = now;
        turn.DecisionDeadline = now + DecisionWindow;
        turn.UpdatedAt = now;
    }

    /// <summary>
    /// Kết thúc lượt bằng việc nhận: tạo quyền tải cho người nhận và đặt lượt thành đã nhận.
    /// Dùng chung cho nút Đồng ý nhận (luồng cũ) và lúc Người xét duyệt gửi biên bản (luồng Biên Bản).
    /// Người gọi tự thêm grant vào repository.
    /// </summary>
    public static HandoverGrant CommitAccept(HandoverTurn turn, Guid recipientUserId, DateTimeOffset now)
    {
        HandoverGrant grant = new HandoverGrant();
        grant.TurnId = turn.Id;
        grant.Turn = turn;
        grant.UserId = recipientUserId;
        grant.Status = HandoverGrantStatus.Active;
        grant.GrantedAt = now;
        grant.ExpiresAt = now + GrantWindow;
        turn.Grant = grant;

        turn.Status = HandoverTurnStatus.HandoverCommitted;
        turn.DecidedAt = now;
        turn.EndedAt = now;
        turn.UpdatedAt = now;
        return grant;
    }

    /// <summary>
    /// Kết thúc lượt bằng việc từ chối. Nội dung của lượt bị xóa ngay thay vì chờ hết thời hạn lưu: hạn xóa đặt đúng lúc này,
    /// job lưu giữ xóa ở vòng quét kế tiếp. Tài sản còn lượt khác tham chiếu thì giữ lại (RETENTION-02).
    /// </summary>
    public static void MarkDeclined(HandoverTurn turn, DateTimeOffset now)
    {
        turn.Status = HandoverTurnStatus.Declined;
        turn.DecidedAt = now;
        turn.EndedAt = now;
        turn.UpdatedAt = now;
        turn.RetentionStatus = RetentionStatus.RetentionActive;
        turn.RetentionStartedAt = now;
        turn.RetentionDeadline = now;
        turn.RetentionRemainingSeconds = null;
    }

    /// <summary>
    /// Lý do grant không dùng được tại thời điểm now (EXPIRED, BLOCKED, REVOKED, PLAN_NOT_ACTIVE);
    /// null nghĩa là dùng được. Kế hoạch đã Closed (mọi lượt xong) vẫn cho tải đến hết hạn grant.
    /// </summary>
    public static string? GrantDeniedReason(HandoverGrant grant, PlanStatus planStatus, DateTimeOffset now)
    {
        HandoverGrantStatus status = EffectiveGrantStatus(grant, now);
        if (status == HandoverGrantStatus.Expired)
        {
            return "EXPIRED";
        }
        if (status == HandoverGrantStatus.Blocked)
        {
            return "BLOCKED";
        }
        if (status == HandoverGrantStatus.Revoked)
        {
            return "REVOKED";
        }

        if (planStatus != PlanStatus.HandoverInProgress && planStatus != PlanStatus.Closed)
        {
            return "PLAN_NOT_ACTIVE";
        }

        return null;
    }

    /// <summary>Mọi lượt của kế hoạch đã kết thúc thì đóng kế hoạch (Closed). Trả true nếu vừa đóng.</summary>
    public static bool CloseIfAllTerminal(HandoverPlan plan, IEnumerable<HandoverTurn> turns, DateTimeOffset now)
    {
        if (plan.Status != PlanStatus.HandoverInProgress)
        {
            return false;
        }

        foreach (HandoverTurn turn in turns)
        {
            if (!IsTerminal(turn.Status))
            {
                return false;
            }
        }

        plan.Status = PlanStatus.Closed;
        plan.UpdatedAt = now;
        return true;
    }
}
