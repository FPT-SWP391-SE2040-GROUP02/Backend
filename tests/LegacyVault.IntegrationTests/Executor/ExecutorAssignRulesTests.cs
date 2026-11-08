using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.ExecutorAssign;

/// <summary>Luật Admin giao hoặc đổi Người thực thi (không cần DB).</summary>
public sealed class ExecutorAssignRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero);

    private static Executor Item(ExecutorStatus status, DateTimeOffset? expiresAt = null)
    {
        Executor item = new Executor();
        item.Status = status;
        item.ExpiresAt = expiresAt ?? Now.AddDays(3);
        return item;
    }

    [Theory]
    [InlineData(PlanStatus.Draft, true)]
    [InlineData(PlanStatus.Active, true)]
    [InlineData(PlanStatus.CheckinOverdue, true)]
    [InlineData(PlanStatus.AwaitingCheck, true)]
    [InlineData(PlanStatus.DeathCaseOpen, false)]
    [InlineData(PlanStatus.HandoverInProgress, false)]
    [InlineData(PlanStatus.Closed, false)]
    [InlineData(PlanStatus.Blocked, false)]
    public void PlanAllowsAssign_OnlyDraftOrRunning(PlanStatus plan, bool expected)
    {
        Assert.Equal(expected, ExecutorAssignRules.PlanAllowsAssign(VaultStatus.Open, plan));
    }

    [Fact]
    public void PlanAllowsAssign_ClosedVaultIsRejected()
    {
        Assert.False(ExecutorAssignRules.PlanAllowsAssign(VaultStatus.Closed, PlanStatus.Active));
    }

    [Fact]
    public void Prepare_RunningPlan_KeepsAcceptedAndReturnsItsIdAsReplaced()
    {
        Executor accepted = Item(ExecutorStatus.Accepted);
        Executor pending = Item(ExecutorStatus.Invited);
        Executor stale = Item(ExecutorStatus.Invited, Now.AddDays(-1));

        Guid? replaces = ExecutorAssignRules.PrepareForAssign(
            new[] { accepted, pending, stale }, PlanStatus.AwaitingCheck, Now);

        Assert.Equal(accepted.Id, replaces);
        Assert.Equal(ExecutorStatus.Accepted, accepted.Status);
        Assert.Equal(ExecutorStatus.Revoked, pending.Status);
        Assert.Equal(ExecutorStatus.Expired, stale.Status);
        Assert.Equal(Now, pending.EndedAt);
    }

    [Fact]
    public void Prepare_DraftPlan_RevokesEverythingAndReplacesNobody()
    {
        Executor accepted = Item(ExecutorStatus.Accepted);
        Executor suspended = Item(ExecutorStatus.Suspended);

        Guid? replaces = ExecutorAssignRules.PrepareForAssign(new[] { accepted, suspended }, PlanStatus.Draft, Now);

        Assert.Null(replaces);
        Assert.Equal(ExecutorStatus.Revoked, accepted.Status);
        Assert.Equal(ExecutorStatus.Revoked, suspended.Status);
    }

    [Fact]
    public void Prepare_FinishedRowsAreLeftAlone()
    {
        Executor declined = Item(ExecutorStatus.Declined);
        Executor revoked = Item(ExecutorStatus.Revoked);

        Guid? replaces = ExecutorAssignRules.PrepareForAssign(new[] { declined, revoked }, PlanStatus.Active, Now);

        Assert.Null(replaces);
        Assert.Equal(ExecutorStatus.Declined, declined.Status);
        Assert.Equal(ExecutorStatus.Revoked, revoked.Status);
        Assert.Null(declined.EndedAt);
    }
}
