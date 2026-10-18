using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Đồng ý, từ chối, danh sách tài sản và đóng kế hoạch (ACCEPT, DOWNLOAD-01).</summary>
public sealed class HandoverDecisionTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverService service;
    private readonly Guid claimantId = Guid.NewGuid();
    private readonly Container container;
    private readonly Designation designation;

    public HandoverDecisionTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = claimantId;
        container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        service = new HandoverService(currentUser, users, vaults, repository, new FakeTokenService(), new FakePolicy(), unitOfWork, time);
    }

    private HandoverTurn AddReadyTurn(string token)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, HandoverTurnStatus.ReadyToAccept, token, claimantId);
        turn.ReadyAt = Now;
        turn.DecisionDeadline = Now.AddDays(7);
        repository.Turns.Add(turn);
        return turn;
    }

    private HandoverGrant AddGrant(HandoverTurn turn, Guid userId, HandoverGrantStatus status, DateTimeOffset expiresAt)
    {
        turn.Status = HandoverTurnStatus.HandoverCommitted;
        HandoverGrant grant = new HandoverGrant
        {
            TurnId = turn.Id,
            Turn = turn,
            UserId = userId,
            Status = status,
            GrantedAt = Now,
            ExpiresAt = expiresAt
        };
        turn.Grant = grant;
        repository.Grants.Add(grant);
        return grant;
    }

    [Fact]
    public async Task Accept_CreatesSingleGrantAndCommits()
    {
        HandoverTurn turn = AddReadyTurn("t1");

        HandoverTurnResponse response = await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.Equal("HANDOVER_COMMITTED", response.Status);
        Assert.Single(repository.Grants);
        HandoverGrant grant = repository.Grants[0];
        Assert.Equal(HandoverGrantStatus.Active, grant.Status);
        Assert.Equal(Now.AddDays(30), grant.ExpiresAt);
        Assert.Equal(claimantId, grant.UserId);
        Assert.Equal("ACTIVE", response.Grant!.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(Now, turn.EndedAt);
    }

    [Fact]
    public async Task Accept_Twice_ReturnsSameGrantWithoutSecond()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        HandoverTurnResponse first = await service.AcceptAsync(turn.Id, CancellationToken.None);
        int saves = unitOfWork.SaveCount;

        HandoverTurnResponse second = await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.Single(repository.Grants);
        Assert.Equal(saves, unitOfWork.SaveCount);
        Assert.Equal(first.Grant!.Id, second.Grant!.Id);
    }

    [Fact]
    public async Task Accept_AtDecisionDeadline_ThrowsDecisionExpired()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        time.Now = turn.DecisionDeadline!.Value;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.AcceptAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverDecisionExpired, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Empty(repository.Grants);
    }

    [Fact]
    public async Task Accept_OneSecondBeforeDeadline_Succeeds()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        time.Now = turn.DecisionDeadline!.Value.AddSeconds(-1);

        HandoverTurnResponse response = await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.Equal("HANDOVER_COMMITTED", response.Status);
    }

    [Fact]
    public async Task Accept_WhenIdentityInProgress_ThrowsNotAllowed()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        turn.Status = HandoverTurnStatus.IdentityInProgress;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.AcceptAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("WRONG_STATE", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Accept_PlanNotInHandover_ThrowsNotAllowed()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        data.Plan.Status = PlanStatus.Blocked;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.AcceptAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("PLAN_NOT_IN_HANDOVER", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Accept_OtherUser_ThrowsNotFound()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.AcceptAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
        Assert.Equal(AppErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task Accept_LastOpenTurn_ClosesPlan()
    {
        HandoverTurn turn = AddReadyTurn("t1");

        await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Closed, data.Plan.Status);
    }

    [Fact]
    public async Task Accept_WhileAnotherTurnStillOpen_KeepsPlanInHandover()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        HandoverTurn other = data.BuildTurn(container, designation, HandoverTurnStatus.Invited, "t2", null);
        repository.Turns.Add(other);

        await service.AcceptAsync(turn.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.HandoverInProgress, data.Plan.Status);
    }

    [Fact]
    public async Task Decline_MovesToDeclinedWithoutGrant()
    {
        HandoverTurn turn = AddReadyTurn("t1");

        HandoverTurnResponse response = await service.DeclineAsync(turn.Id, CancellationToken.None);

        Assert.Equal("DECLINED", response.Status);
        Assert.Equal(Now, turn.EndedAt);
        Assert.Equal(Now, turn.DecidedAt);
        Assert.Empty(repository.Grants);
    }

    [Fact]
    public async Task Decline_NotReady_ThrowsNotAllowed()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        turn.Status = HandoverTurnStatus.IdentityInProgress;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.DeclineAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("WRONG_STATE", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Decline_LastOpenTurn_ClosesPlan()
    {
        HandoverTurn turn = AddReadyTurn("t1");

        await service.DeclineAsync(turn.Id, CancellationToken.None);

        Assert.Equal(PlanStatus.Closed, data.Plan.Status);
    }

    [Fact]
    public async Task GetTurn_AfterCommit_ShowsGrantExpiredAtExactExpiryEvenIfStoredActive()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        AddGrant(turn, claimantId, HandoverGrantStatus.Active, Now.AddDays(30));
        time.Now = Now.AddDays(30);

        HandoverTurnResponse response = await service.GetTurnAsync(turn.Id, CancellationToken.None);

        Assert.Equal("EXPIRED", response.Grant!.Status);
    }

    [Fact]
    public async Task ListAssets_ReturnsSnapshotItemsOfOwnActiveGrant()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        HandoverGrant grant = AddGrant(turn, claimantId, HandoverGrantStatus.Active, Now.AddDays(30));

        List<HandoverItemResponse> items = await service.ListAssetsAsync(grant.Id, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(turn.Items[0].Id, items[0].Id);
        Assert.Equal("FILE", items[0].Kind);
    }

    [Fact]
    public async Task ListAssets_ExpiredGrant_ThrowsGrantNotActive()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        HandoverGrant grant = AddGrant(turn, claimantId, HandoverGrantStatus.Active, Now);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ListAssetsAsync(grant.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.GrantNotActive, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
        Assert.Equal("EXPIRED", error.Extensions["reason"]);
    }

    [Fact]
    public async Task ListAssets_BlockedGrant_Throws()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        HandoverGrant grant = AddGrant(turn, claimantId, HandoverGrantStatus.Blocked, Now.AddDays(30));

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ListAssetsAsync(grant.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.GrantNotActive, error.Code);
        Assert.Equal("BLOCKED", error.Extensions["reason"]);
    }

    [Fact]
    public async Task ListAssets_OtherUser_ThrowsNotFound()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        HandoverGrant grant = AddGrant(turn, Guid.NewGuid(), HandoverGrantStatus.Active, Now.AddDays(30));

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ListAssetsAsync(grant.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task Accept_LosingTheRaceOnUniqueGrant_ThrowsEditConflict()
    {
        HandoverTurn turn = AddReadyTurn("t1");
        LegacyVault.Application.Repositories.IUnitOfWork failing = ThrowingUnitOfWork.Create(new Microsoft.EntityFrameworkCore.DbUpdateException("trùng grant"));
        HandoverService racing = new HandoverService(currentUser, users, vaults, repository, new FakeTokenService(), new FakePolicy(), failing, time);

        AppException error = await Assert.ThrowsAsync<AppException>(() => racing.AcceptAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.EditConflict, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
    }
}
