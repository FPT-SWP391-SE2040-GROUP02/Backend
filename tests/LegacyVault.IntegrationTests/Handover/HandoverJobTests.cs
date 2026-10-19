using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Job hết hạn: phản hồi, quyết định, grant (TIME-02, TIME-05).</summary>
public sealed class HandoverJobTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverJob job;
    private readonly Container container;
    private readonly Designation designation;

    public HandoverJobTests()
    {
        vaults.Vaults.Add(data.Vault);
        container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        job = new HandoverJob(vaults, repository, holds, unitOfWork, time);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status, string token)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, token, null);
        repository.Turns.Add(turn);
        return turn;
    }

    [Fact]
    public async Task ResponseExpiry_MovesInvitedToInitialResponseExpired_AtExactDeadline()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, "t1");

        time.Now = turn.ResponseDeadline.AddSeconds(-1);
        await job.ProcessResponseExpiryAsync(turn.Id, CancellationToken.None);
        Assert.Equal(HandoverTurnStatus.Invited, turn.Status);

        time.Now = turn.ResponseDeadline;
        await job.ProcessResponseExpiryAsync(turn.Id, CancellationToken.None);
        Assert.Equal(HandoverTurnStatus.InitialResponseExpired, turn.Status);

        int saves = unitOfWork.SaveCount;
        await job.ProcessResponseExpiryAsync(turn.Id, CancellationToken.None);
        Assert.Equal(saves, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ResponseExpiry_OtherStatusUntouched()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, "t1");
        time.Now = turn.ResponseDeadline.AddDays(1);

        await job.ProcessResponseExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task ResponseExpiry_DoesNotCloseThePlan()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, "t1");
        time.Now = turn.ResponseDeadline;

        await job.ProcessResponseExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.HandoverInProgress, data.Plan.Status);
    }

    [Fact]
    public async Task DecisionExpiry_MovesReadyToDecisionExpiredAndSetsEndedAt()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept, "t1");
        turn.DecisionDeadline = Now.AddDays(7);
        HandoverTurn other = AddTurn(HandoverTurnStatus.Invited, "t2");

        time.Now = Now.AddDays(7).AddSeconds(-1);
        await job.ProcessDecisionExpiryAsync(turn.Id, CancellationToken.None);
        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);

        time.Now = Now.AddDays(7);
        await job.ProcessDecisionExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.DecisionExpired, turn.Status);
        Assert.Equal(time.Now, turn.EndedAt);
        Assert.Equal(PlanStatus.HandoverInProgress, data.Plan.Status);
        Assert.Equal(HandoverTurnStatus.Invited, other.Status);
    }

    [Fact]
    public async Task DecisionExpiry_LastOpenTurn_ClosesPlan()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept, "t1");
        turn.DecisionDeadline = Now.AddDays(7);
        time.Now = Now.AddDays(8);

        await job.ProcessDecisionExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Closed, data.Plan.Status);
    }

    [Fact]
    public async Task GrantExpiry_MovesActiveToExpiredAndSetsEndedAt()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.HandoverCommitted, "t1");
        HandoverGrant grant = new HandoverGrant
        {
            TurnId = turn.Id, Turn = turn, UserId = Guid.NewGuid(),
            Status = HandoverGrantStatus.Active, GrantedAt = Now, ExpiresAt = Now.AddDays(30)
        };
        repository.Grants.Add(grant);

        time.Now = grant.ExpiresAt.AddSeconds(-1);
        await job.ProcessGrantExpiryAsync(grant.Id, CancellationToken.None);
        Assert.Equal(HandoverGrantStatus.Active, grant.Status);

        time.Now = grant.ExpiresAt;
        await job.ProcessGrantExpiryAsync(grant.Id, CancellationToken.None);

        Assert.Equal(HandoverGrantStatus.Expired, grant.Status);
        Assert.Equal(time.Now, grant.EndedAt);
    }

    [Fact]
    public async Task GrantExpiry_BlockedGrantUntouched()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.HandoverCommitted, "t1");
        HandoverGrant grant = new HandoverGrant
        {
            TurnId = turn.Id, Turn = turn, UserId = Guid.NewGuid(),
            Status = HandoverGrantStatus.Blocked, GrantedAt = Now, ExpiresAt = Now.AddDays(30)
        };
        repository.Grants.Add(grant);
        time.Now = Now.AddDays(60);

        await job.ProcessGrantExpiryAsync(grant.Id, CancellationToken.None);

        Assert.Equal(HandoverGrantStatus.Blocked, grant.Status);
    }

    [Fact]
    public async Task FindExpired_ReturnOnlyDueIds()
    {
        HandoverTurn due = AddTurn(HandoverTurnStatus.Invited, "t1");
        HandoverTurn notDue = AddTurn(HandoverTurnStatus.Invited, "t2");
        notDue.ResponseDeadline = Now.AddDays(9);
        HandoverTurn ready = AddTurn(HandoverTurnStatus.ReadyToAccept, "t3");
        ready.DecisionDeadline = Now.AddDays(7);
        HandoverTurn committed = AddTurn(HandoverTurnStatus.HandoverCommitted, "t4");
        HandoverGrant grant = new HandoverGrant
        {
            TurnId = committed.Id, Turn = committed, UserId = Guid.NewGuid(),
            Status = HandoverGrantStatus.Active, GrantedAt = Now, ExpiresAt = Now.AddDays(30)
        };
        repository.Grants.Add(grant);

        time.Now = Now.AddDays(5);
        IReadOnlyList<Guid> responseIds = await job.FindExpiredResponseTurnIdsAsync(CancellationToken.None);
        Assert.Single(responseIds);
        Assert.Equal(due.Id, responseIds[0]);
        Assert.Empty(await job.FindExpiredDecisionTurnIdsAsync(CancellationToken.None));
        Assert.Empty(await job.FindExpiredGrantIdsAsync(CancellationToken.None));

        time.Now = Now.AddDays(30);
        IReadOnlyList<Guid> decisionIds = await job.FindExpiredDecisionTurnIdsAsync(CancellationToken.None);
        IReadOnlyList<Guid> grantIds = await job.FindExpiredGrantIdsAsync(CancellationToken.None);
        Assert.Equal(ready.Id, Assert.Single(decisionIds));
        Assert.Equal(grant.Id, Assert.Single(grantIds));
    }

    // H1, TIME-04, TIME-09: hold hoặc kế hoạch Blocked thì đồng hồ tạm dừng, job không cho hết hạn.

    [Fact]
    public async Task ResponseExpiry_ActiveHold_DoesNothing()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, "t1");
        holds.Active = true;
        time.Now = turn.ResponseDeadline.AddDays(1);

        await job.ProcessResponseExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.Invited, turn.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task DecisionExpiry_ActiveHold_DoesNothing()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept, "t1");
        turn.DecisionDeadline = Now.AddDays(7);
        holds.Active = true;
        time.Now = Now.AddDays(9);

        await job.ProcessDecisionExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);
        Assert.Null(turn.EndedAt);
        Assert.Equal(PlanStatus.HandoverInProgress, data.Plan.Status);
    }

    [Fact]
    public async Task DecisionExpiry_BlockedPlanWithoutHoldRecord_DoesNothing()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept, "t1");
        turn.DecisionDeadline = Now.AddDays(7);
        data.Plan.Status = PlanStatus.Blocked;
        time.Now = Now.AddDays(9);

        await job.ProcessDecisionExpiryAsync(turn.Id, CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);
        Assert.Equal(PlanStatus.Blocked, data.Plan.Status);
    }

    [Fact]
    public async Task GrantExpiry_ActiveHold_DoesNothing()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.HandoverCommitted, "t1");
        HandoverGrant grant = new HandoverGrant
        {
            TurnId = turn.Id, Turn = turn, UserId = Guid.NewGuid(),
            Status = HandoverGrantStatus.Active, GrantedAt = Now, ExpiresAt = Now.AddDays(30)
        };
        repository.Grants.Add(grant);
        holds.Active = true;
        time.Now = grant.ExpiresAt.AddDays(1);

        await job.ProcessGrantExpiryAsync(grant.Id, CancellationToken.None);

        Assert.Equal(HandoverGrantStatus.Active, grant.Status);
        Assert.Null(grant.EndedAt);
    }
}
