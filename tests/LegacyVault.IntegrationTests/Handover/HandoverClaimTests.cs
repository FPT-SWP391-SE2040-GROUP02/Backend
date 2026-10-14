using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Kiểm link, nhận lượt và xem lượt (INVITE, CLAIM, SEC-04).</summary>
public sealed class HandoverClaimTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private const string Token = "token-1";

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FakePolicy policy = new FakePolicy();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverService service;
    private readonly User claimant;
    private readonly Container container;
    private readonly Designation designation;

    public HandoverClaimTests()
    {
        claimant = new User { Email = "nguoi-nhan@example.com", FullName = "Người Nhận", Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-10) };
        users.Users.Add(claimant);
        users.Users.Add(data.Owner);
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = claimant.Id;

        container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");

        service = new HandoverService(currentUser, users, vaults, repository, new FakeTokenService(), policy, unitOfWork, time);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status, Guid? claimantId)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, Token, claimantId);
        repository.Turns.Add(turn);
        return turn;
    }

    [Fact]
    public async Task GetInvitation_UnknownToken_ThrowsInvitationInvalid()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.GetInvitationAsync("sai", CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverInvitationInvalid, error.Code);
        Assert.Equal(AppErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task GetInvitation_Known_ReturnsValidWithoutOtherData()
    {
        AddTurn(HandoverTurnStatus.Invited, null);

        InvitationStatusResponse response = await service.GetInvitationAsync(Token, CancellationToken.None);

        Assert.True(response.Valid);
        Assert.False(response.ResponseExpired);
    }

    [Fact]
    public async Task GetInvitation_AfterResponseDeadline_ReportsResponseExpired()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        turn.ResponseDeadline = Now;

        InvitationStatusResponse response = await service.GetInvitationAsync(Token, CancellationToken.None);

        Assert.True(response.ResponseExpired);
    }

    [Fact]
    public async Task Claim_PolicyOn_StopsAtIdentityInProgress()
    {
        policy.RequireIdentityVerification = true;
        AddTurn(HandoverTurnStatus.Invited, null);

        HandoverTurnResponse response = await service.ClaimAsync(Token, CancellationToken.None);

        HandoverTurn turn = repository.Turns[0];
        Assert.Equal("IDENTITY_IN_PROGRESS", response.Status);
        Assert.Equal(claimant.Id, turn.ClaimantUserId);
        Assert.Equal(Now, turn.ClaimedAt);
        Assert.False(response.IdentityBypassed);
        Assert.Null(response.Container);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Claim_PolicyOff_GoesStraightToReadyWithBypassFlag()
    {
        policy.RequireIdentityVerification = false;
        AddTurn(HandoverTurnStatus.Invited, null);

        HandoverTurnResponse response = await service.ClaimAsync(Token, CancellationToken.None);

        Assert.Equal("READY_TO_ACCEPT", response.Status);
        Assert.True(response.IdentityBypassed);
        Assert.Equal(Now.AddDays(7), response.DecisionDeadline);
        Assert.Equal(Now, repository.Turns[0].ReadyAt);
        Assert.NotNull(response.Container);
        Assert.Equal("Gói A", response.Container!.Name);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task Claim_AfterResponseExpired_StillWorks()
    {
        policy.RequireIdentityVerification = true;
        AddTurn(HandoverTurnStatus.InitialResponseExpired, null);

        HandoverTurnResponse response = await service.ClaimAsync(Token, CancellationToken.None);

        Assert.Equal("IDENTITY_IN_PROGRESS", response.Status);
    }

    [Fact]
    public async Task Claim_SameUserTwice_IsIdempotent()
    {
        policy.RequireIdentityVerification = true;
        AddTurn(HandoverTurnStatus.Invited, null);
        await service.ClaimAsync(Token, CancellationToken.None);
        int saves = unitOfWork.SaveCount;
        DateTimeOffset? claimedAt = repository.Turns[0].ClaimedAt;
        time.Now = Now.AddHours(3);

        HandoverTurnResponse second = await service.ClaimAsync(Token, CancellationToken.None);

        Assert.Equal("IDENTITY_IN_PROGRESS", second.Status);
        Assert.Equal(claimedAt, repository.Turns[0].ClaimedAt);
        Assert.Equal(saves, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Claim_OtherUserAlreadyClaimed_ThrowsAlreadyClaimed()
    {
        AddTurn(HandoverTurnStatus.IdentityInProgress, Guid.NewGuid());

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverAlreadyClaimed, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
    }

    [Fact]
    public async Task Claim_ByVaultOwner_ThrowsNotAllowed()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        currentUser.UserId = data.Owner.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("OWNER_CANNOT_CLAIM", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Claim_EmailNotVerified_ThrowsEmailNotVerified()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        claimant.EmailVerifiedAt = null;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.EmailNotVerified, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
    }

    [Fact]
    public async Task Claim_InactiveAccount_ThrowsAccountNotActive()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        claimant.Status = UserStatus.Locked;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.AccountNotActive, error.Code);
    }

    [Fact]
    public async Task Claim_Unauthenticated_ThrowsSessionExpired()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        currentUser.UserId = null;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.SessionExpired, error.Code);
        Assert.Equal(AppErrorType.Unauthorized, error.Type);
    }

    [Fact]
    public async Task Claim_PlanNotInHandover_ThrowsNotAllowed()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        data.Plan.Status = PlanStatus.Closed;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("PLAN_NOT_IN_HANDOVER", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Claim_TerminalTurn_ThrowsNotAllowed()
    {
        AddTurn(HandoverTurnStatus.Cancelled, null);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("WRONG_STATE", error.Extensions["reason"]);
    }

    [Fact]
    public async Task Claim_UnknownToken_ThrowsInvitationInvalid()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync("khong-co", CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverInvitationInvalid, error.Code);
    }

    [Fact]
    public async Task GetTurn_OfAnotherUser_ThrowsNotFound_SameAsUnknownId()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, Guid.NewGuid());

        AppException other = await Assert.ThrowsAsync<AppException>(() => service.GetTurnAsync(turn.Id, CancellationToken.None));
        AppException unknown = await Assert.ThrowsAsync<AppException>(() => service.GetTurnAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, other.Code);
        Assert.Equal(AppErrorType.NotFound, other.Type);
        Assert.Equal(unknown.Code, other.Code);
        Assert.Equal(unknown.Message, other.Message);
    }

    [Fact]
    public async Task GetMyTurns_ReturnsOnlyMineNewestFirst()
    {
        HandoverTurn older = AddTurn(HandoverTurnStatus.IdentityInProgress, claimant.Id);
        older.InvitedAt = Now.AddDays(-2);
        HandoverTurn newer = data.BuildTurn(container, designation, HandoverTurnStatus.IdentityInProgress, "token-2", claimant.Id);
        repository.Turns.Add(newer);
        HandoverTurn notMine = data.BuildTurn(container, designation, HandoverTurnStatus.IdentityInProgress, "token-3", Guid.NewGuid());
        repository.Turns.Add(notMine);

        List<HandoverTurnResponse> mine = await service.GetMyTurnsAsync(CancellationToken.None);

        Assert.Equal(2, mine.Count);
        Assert.Equal(newer.Id, mine[0].Id);
        Assert.Equal(older.Id, mine[1].Id);
    }
}
