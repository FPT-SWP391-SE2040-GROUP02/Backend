using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Luật thuần: claim đang xử lý chỉ giữ dữ liệu tối đa 30 ngày (CLAIM-06, RETENTION-04, M1).</summary>
public sealed class RetentionClaimWindowRulesTests
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
    public void ClaimHold_IdentityInProgressWithinThirtyDays_HoldsData()
    {
        HandoverTurn turn = Turn(HandoverTurnStatus.IdentityInProgress);
        turn.ClaimedAt = Now;

        string? reason = RetentionRules.HoldReason(turn, PlanStatus.HandoverInProgress, Now.AddDays(30).AddSeconds(-1));

        Assert.Equal(RetentionRules.HoldClaimInProgress, reason);
    }

    [Fact]
    public void ClaimHold_IdentityInProgressAfterThirtyDays_NoLongerHolds_SoTurnIsNotStuckForever()
    {
        // M1, CLAIM-06: đúng giây thứ 30 ngày là hết giữ, đồng hồ lưu chạy tiếp
        HandoverTurn turn = Turn(HandoverTurnStatus.IdentityInProgress);
        turn.ClaimedAt = Now;

        string? reason = RetentionRules.HoldReason(turn, PlanStatus.HandoverInProgress, Now.AddDays(30));

        Assert.Null(reason);
    }

    [Fact]
    public void ClaimHold_WithoutClaimedAt_FallsBackToInvitedAt()
    {
        HandoverTurn turn = Turn(HandoverTurnStatus.IdentityInProgress);

        Assert.Null(RetentionRules.HoldReason(turn, PlanStatus.HandoverInProgress, Now.AddDays(31)));
        Assert.Equal(RetentionRules.HoldClaimInProgress,
            RetentionRules.HoldReason(turn, PlanStatus.HandoverInProgress, Now.AddDays(10)));
    }
}
