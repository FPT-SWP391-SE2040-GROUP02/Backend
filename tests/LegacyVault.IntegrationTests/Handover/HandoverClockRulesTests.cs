using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Luật thuần: dời hạn của lượt và grant sau khi gỡ hold (TIME-04, H1).</summary>
public sealed class HandoverClockAndRetentionRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private static HandoverTurn Turn(HandoverTurnStatus status)
    {
        HandoverTurn turn = new HandoverTurn();
        turn.Status = status;
        turn.InvitedAt = Now;
        turn.ResponseDeadline = Now.AddDays(5);
        return turn;
    }

    [Fact]
    public void ExtendAfterPause_ShiftsRunningDeadlinesByPausedDuration()
    {
        HandoverTurn invited = Turn(HandoverTurnStatus.Invited);
        HandoverTurn ready = Turn(HandoverTurnStatus.ReadyToAccept);
        ready.DecisionDeadline = Now.AddDays(7);
        HandoverTurn committed = Turn(HandoverTurnStatus.HandoverCommitted);
        committed.Grant = new HandoverGrant { Status = HandoverGrantStatus.Active, ExpiresAt = Now.AddDays(30) };

        // Dừng từ ngày 1 đến ngày 4: 3 ngày
        int shifted = HandoverClockRules.ExtendAfterPause(
            new List<HandoverTurn> { invited, ready, committed }, Now.AddDays(1), Now.AddDays(4));

        Assert.Equal(3, shifted);
        Assert.Equal(Now.AddDays(8), invited.ResponseDeadline);
        Assert.Equal(Now.AddDays(10), ready.DecisionDeadline);
        Assert.Equal(Now.AddDays(33), committed.Grant!.ExpiresAt);
    }

    [Fact]
    public void ExtendAfterPause_DoesNotTouchDeadlinesThatAlreadyPassedBeforePause()
    {
        HandoverTurn ready = Turn(HandoverTurnStatus.ReadyToAccept);
        ready.DecisionDeadline = Now.AddDays(2);

        int shifted = HandoverClockRules.ExtendAfterPause(new List<HandoverTurn> { ready }, Now.AddDays(3), Now.AddDays(5));

        Assert.Equal(0, shifted);
        Assert.Equal(Now.AddDays(2), ready.DecisionDeadline);
    }

    [Fact]
    public void ExtendAfterPause_SkipsTerminalTurnsAndExpiredOrRevokedGrants()
    {
        HandoverTurn declined = Turn(HandoverTurnStatus.Declined);
        HandoverTurn revoked = Turn(HandoverTurnStatus.HandoverCommitted);
        revoked.Grant = new HandoverGrant { Status = HandoverGrantStatus.Revoked, ExpiresAt = Now.AddDays(30) };
        HandoverTurn expired = Turn(HandoverTurnStatus.HandoverCommitted);
        expired.Grant = new HandoverGrant { Status = HandoverGrantStatus.Expired, ExpiresAt = Now.AddDays(30) };

        int shifted = HandoverClockRules.ExtendAfterPause(
            new List<HandoverTurn> { declined, revoked, expired }, Now.AddDays(1), Now.AddDays(4));

        Assert.Equal(0, shifted);
        Assert.Equal(Now.AddDays(30), revoked.Grant!.ExpiresAt);
        Assert.Equal(Now.AddDays(30), expired.Grant!.ExpiresAt);
    }

    [Fact]
    public void ExtendAfterPause_BlockedGrantIsShifted_SoItIsNotLostWhileBlocked()
    {
        HandoverTurn committed = Turn(HandoverTurnStatus.HandoverCommitted);
        committed.Grant = new HandoverGrant { Status = HandoverGrantStatus.Blocked, ExpiresAt = Now.AddDays(30) };

        HandoverClockRules.ExtendAfterPause(new List<HandoverTurn> { committed }, Now.AddDays(2), Now.AddDays(6));

        Assert.Equal(Now.AddDays(34), committed.Grant.ExpiresAt);
    }

    [Fact]
    public void ExtendAfterPause_ZeroOrNegativePause_ChangesNothing()
    {
        HandoverTurn invited = Turn(HandoverTurnStatus.Invited);

        int shifted = HandoverClockRules.ExtendAfterPause(new List<HandoverTurn> { invited }, Now.AddDays(2), Now.AddDays(2));

        Assert.Equal(0, shifted);
        Assert.Equal(Now.AddDays(5), invited.ResponseDeadline);
    }

    [Fact]
    public void FindPauseStart_MergesOverlappingHoldsIntoOnePause()
    {
        Hold first = new Hold { CreatedAt = Now.AddDays(1), ReleasedAt = Now.AddDays(4), Status = HoldStatus.Released };
        Hold second = new Hold { CreatedAt = Now.AddDays(3), ReleasedAt = Now.AddDays(6), Status = HoldStatus.Released };
        Hold unrelated = new Hold { CreatedAt = Now.AddDays(-10), ReleasedAt = Now.AddDays(-8), Status = HoldStatus.Released };

        DateTimeOffset start = HandoverClockRules.FindPauseStart(second, new List<Hold> { first, second, unrelated }, Now.AddDays(6));

        Assert.Equal(Now.AddDays(1), start);
    }

    [Fact]
    public void FindPauseStart_SeparateHolds_UsesOnlyTheReleasedOne()
    {
        Hold earlier = new Hold { CreatedAt = Now.AddDays(1), ReleasedAt = Now.AddDays(2), Status = HoldStatus.Released };
        Hold later = new Hold { CreatedAt = Now.AddDays(5), ReleasedAt = Now.AddDays(6), Status = HoldStatus.Released };

        DateTimeOffset start = HandoverClockRules.FindPauseStart(later, new List<Hold> { earlier, later }, Now.AddDays(6));

        Assert.Equal(Now.AddDays(5), start);
    }
}
