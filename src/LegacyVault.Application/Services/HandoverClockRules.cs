using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Luật thuần dời hạn của giai đoạn nhận tài sản sau khi hold được gỡ (TIME-04, TIME-09).
/// Trong lúc có hold, HandoverJob không cho lượt hay grant hết hạn; khi gỡ hold, các hạn còn chạy được dời đúng bằng
/// thời gian đã dừng để người nhận vẫn còn đủ phần thời gian còn lại, không đặt lại từ đầu.
/// </summary>
public static class HandoverClockRules
{
    /// <summary>
    /// Điểm bắt đầu của đợt dừng liên tục chứa hold vừa gỡ: lùi tới hold sớm nhất mà thời gian hiệu lực nối liền với đợt này.
    /// Hai hold chồng nhau chỉ được tính một lần.
    /// </summary>
    public static DateTimeOffset FindPauseStart(Hold released, IEnumerable<Hold> vaultHolds, DateTimeOffset now)
    {
        DateTimeOffset start = released.CreatedAt;
        List<Hold> others = vaultHolds.Where(h => h.Id != released.Id).ToList();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Hold other in others)
            {
                DateTimeOffset otherEnd = other.ReleasedAt ?? now;
                if (other.CreatedAt < start && otherEnd >= start)
                {
                    start = other.CreatedAt;
                    changed = true;
                }
            }
        }
        return start;
    }

    /// <summary>
    /// Dời hạn phản hồi, hạn quyết định và hạn grant của các lượt chưa kết thúc đúng bằng khoảng [pausedFrom, resumedAt].
    /// Chỉ dời hạn còn nằm sau pausedFrom (hạn đã qua trước khi dừng thì đã xử lý rồi). Trả số hạn đã dời.
    /// </summary>
    public static int ExtendAfterPause(
        IEnumerable<HandoverTurn> turns, DateTimeOffset pausedFrom, DateTimeOffset resumedAt)
    {
        TimeSpan paused = resumedAt - pausedFrom;
        if (paused <= TimeSpan.Zero)
        {
            return 0;
        }

        int shifted = 0;
        foreach (HandoverTurn turn in turns)
        {
            if (HandoverRules.IsTerminal(turn.Status))
            {
                // Lượt đã kết thúc không còn hạn; grant của lượt đã nhận vẫn có hạn riêng, xét ở dưới.
                if (turn.Status == HandoverTurnStatus.HandoverCommitted)
                {
                    shifted += ShiftGrant(turn, pausedFrom, paused);
                }
                continue;
            }

            if (turn.Status == HandoverTurnStatus.Invited && turn.ResponseDeadline > pausedFrom)
            {
                turn.ResponseDeadline = turn.ResponseDeadline + paused;
                turn.UpdatedAt = resumedAt;
                shifted++;
            }
            if (turn.Status == HandoverTurnStatus.ReadyToAccept
                && turn.DecisionDeadline != null && turn.DecisionDeadline.Value > pausedFrom)
            {
                turn.DecisionDeadline = turn.DecisionDeadline.Value + paused;
                turn.UpdatedAt = resumedAt;
                shifted++;
            }
        }
        return shifted;
    }

    private static int ShiftGrant(HandoverTurn turn, DateTimeOffset pausedFrom, TimeSpan paused)
    {
        HandoverGrant? grant = turn.Grant;
        if (grant == null || grant.ExpiresAt <= pausedFrom)
        {
            return 0;
        }
        if (grant.Status != HandoverGrantStatus.Active && grant.Status != HandoverGrantStatus.Blocked)
        {
            return 0;
        }
        grant.ExpiresAt = grant.ExpiresAt + paused;
        return 1;
    }
}
