using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Luật thời gian và trạng thái thuần của giai đoạn nhận tài sản.</summary>
public sealed class HandoverRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Windows_AreFiveSevenThirtyDays()
    {
        Assert.Equal(TimeSpan.FromDays(5), HandoverRules.ResponseWindow);
        Assert.Equal(TimeSpan.FromDays(7), HandoverRules.DecisionWindow);
        Assert.Equal(TimeSpan.FromDays(30), HandoverRules.GrantWindow);
    }

    [Fact]
    public void IsTerminal_ExactlyFiveStatuses()
    {
        HandoverTurnStatus[] terminal =
        {
            HandoverTurnStatus.HandoverCommitted,
            HandoverTurnStatus.Declined,
            HandoverTurnStatus.DecisionExpired,
            HandoverTurnStatus.Cancelled,
            HandoverTurnStatus.Lapsed
        };

        foreach (HandoverTurnStatus status in Enum.GetValues<HandoverTurnStatus>())
        {
            bool expected = Array.IndexOf(terminal, status) >= 0;
            Assert.Equal(expected, HandoverRules.IsTerminal(status));
        }
    }

    [Fact]
    public void CanClaim_OnlyInvitedAndInitialResponseExpired()
    {
        foreach (HandoverTurnStatus status in Enum.GetValues<HandoverTurnStatus>())
        {
            bool expected = status == HandoverTurnStatus.Invited || status == HandoverTurnStatus.InitialResponseExpired;
            Assert.Equal(expected, HandoverRules.CanClaim(status));
        }
    }

    [Fact]
    public void IsExpired_UsesGreaterOrEqual()
    {
        DateTimeOffset deadline = Now;

        Assert.False(HandoverRules.IsExpired(deadline, deadline.AddSeconds(-1)));
        Assert.True(HandoverRules.IsExpired(deadline, deadline));
        Assert.True(HandoverRules.IsExpired(deadline, deadline.AddSeconds(1)));
    }

    [Fact]
    public void EffectiveGrantStatus_ActiveBecomesExpiredAtExactExpiry()
    {
        HandoverGrant grant = new HandoverGrant { Status = HandoverGrantStatus.Active, ExpiresAt = Now };

        Assert.Equal(HandoverGrantStatus.Active, HandoverRules.EffectiveGrantStatus(grant, Now.AddSeconds(-1)));
        Assert.Equal(HandoverGrantStatus.Expired, HandoverRules.EffectiveGrantStatus(grant, Now));
    }

    [Fact]
    public void EffectiveGrantStatus_BlockedAndRevokedStayEvenAfterExpiry()
    {
        HandoverGrant blocked = new HandoverGrant { Status = HandoverGrantStatus.Blocked, ExpiresAt = Now };
        HandoverGrant revoked = new HandoverGrant { Status = HandoverGrantStatus.Revoked, ExpiresAt = Now };

        Assert.Equal(HandoverGrantStatus.Blocked, HandoverRules.EffectiveGrantStatus(blocked, Now.AddDays(1)));
        Assert.Equal(HandoverGrantStatus.Revoked, HandoverRules.EffectiveGrantStatus(revoked, Now.AddDays(1)));
    }

    [Fact]
    public void MarkReadyToAccept_SetsFields()
    {
        HandoverTurn turn = new HandoverTurn { Status = HandoverTurnStatus.IdentityInProgress };

        HandoverRules.MarkReadyToAccept(turn, Now);

        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);
        Assert.Equal(Now, turn.ReadyAt);
        Assert.Equal(Now.AddDays(7), turn.DecisionDeadline);
        Assert.Equal(Now, turn.UpdatedAt);
    }

    [Fact]
    public void GrantDeniedReason_CoversEveryCase()
    {
        HandoverGrant active = new HandoverGrant { Status = HandoverGrantStatus.Active, ExpiresAt = Now.AddDays(1) };

        Assert.Null(HandoverRules.GrantDeniedReason(active, PlanStatus.HandoverInProgress, Now));
        Assert.Null(HandoverRules.GrantDeniedReason(active, PlanStatus.Closed, Now));
        Assert.Equal("PLAN_NOT_ACTIVE", HandoverRules.GrantDeniedReason(active, PlanStatus.DeathCaseOpen, Now));
        Assert.Equal("EXPIRED", HandoverRules.GrantDeniedReason(active, PlanStatus.Closed, Now.AddDays(1)));
        Assert.Equal("BLOCKED", HandoverRules.GrantDeniedReason(
            new HandoverGrant { Status = HandoverGrantStatus.Blocked, ExpiresAt = Now.AddDays(1) }, PlanStatus.Closed, Now));
        Assert.Equal("REVOKED", HandoverRules.GrantDeniedReason(
            new HandoverGrant { Status = HandoverGrantStatus.Revoked, ExpiresAt = Now.AddDays(1) }, PlanStatus.Closed, Now));
    }

    [Fact]
    public void CloseIfAllTerminal_ClosesOnlyWhenEveryTurnIsFinished()
    {
        HandoverPlan plan = new HandoverPlan { Status = PlanStatus.HandoverInProgress };
        HandoverTurn done = new HandoverTurn { Status = HandoverTurnStatus.Declined };
        HandoverTurn open = new HandoverTurn { Status = HandoverTurnStatus.ReadyToAccept };

        Assert.False(HandoverRules.CloseIfAllTerminal(plan, new[] { done, open }, Now));
        Assert.Equal(PlanStatus.HandoverInProgress, plan.Status);

        open.Status = HandoverTurnStatus.HandoverCommitted;
        Assert.True(HandoverRules.CloseIfAllTerminal(plan, new[] { done, open }, Now));
        Assert.Equal(PlanStatus.Closed, plan.Status);
        Assert.Equal(Now, plan.UpdatedAt);
    }
}
