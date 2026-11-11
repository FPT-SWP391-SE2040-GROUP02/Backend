using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.Ekyc;
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
    private readonly FakeHoldReader holdReader = new FakeHoldReader();
    private readonly FakeEkycBeneficiaryRepository beneficiaryRepository = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
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
        RegisterRecipient(designation);

        // Cần kho hồ sơ người nhận và bộ giải mã giả để kiểm email người nhận (D2).
        service = new HandoverService(currentUser, users, vaults, repository, new FakeTokenService(), policy, holdReader,
            unitOfWork, time, null, null, beneficiaryRepository, protector);
    }

    /// <summary>Đưa hồ sơ người nhận của chỉ định vào kho giả để service đọc được email.</summary>
    private void RegisterRecipient(Designation target)
    {
        beneficiaryRepository.Beneficiaries.Add(target.Beneficiary);
        protector.Data[target.BeneficiaryId] = data.People[target.BeneficiaryId];
    }

    /// <summary>Thêm một tài khoản đã xác minh với email cho trước và chuyển người đang đăng nhập sang tài khoản đó.</summary>
    private User SignInAs(string email)
    {
        User other = new User { Email = email, FullName = "Người Khác", Status = UserStatus.Active, EmailVerifiedAt = Now.AddDays(-5) };
        users.Users.Add(other);
        currentUser.UserId = other.Id;
        return other;
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
    public async Task GetInvitation_ReturnsMaskedEmailAndAccountExists_WithoutFullEmail()
    {
        AddTurn(HandoverTurnStatus.Invited, null);

        InvitationStatusResponse response = await service.GetInvitationAsync(Token, CancellationToken.None);

        // "nguoi-nhan" có 10 ký tự: giữ chữ đầu và cuối, che 8 ký tự giữa.
        string expected = "n" + new string('•', 8) + "n@example.com";
        Assert.Equal(expected, response.InvitedEmailMasked);
        Assert.Equal("nguoi-nhan@example.com", response.InvitedEmail);
        Assert.True(response.AccountExists);
        Assert.Equal("Chủ Kho", response.OwnerFullName);
    }

    [Fact]
    public async Task GetInvitation_NoAccountForInvitedEmail_AccountExistsFalse()
    {
        users.Users.Remove(claimant);
        AddTurn(HandoverTurnStatus.Invited, null);

        InvitationStatusResponse response = await service.GetInvitationAsync(Token, CancellationToken.None);

        Assert.False(response.AccountExists);
        Assert.NotNull(response.InvitedEmailMasked);
    }

    [Fact]
    public async Task GetInvitation_RecipientEmailWithUpperCaseAndSpaces_MatchesAccountCaseInsensitively()
    {
        data.People[designation.BeneficiaryId].Email = "  Nguoi-Nhan@Example.com ";
        AddTurn(HandoverTurnStatus.Invited, null);

        InvitationStatusResponse response = await service.GetInvitationAsync(Token, CancellationToken.None);

        Assert.True(response.AccountExists);
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
    public async Task Claim_ByToken_WrongEmail_ThrowsEmailMismatchWithMaskedEmail()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        User other = SignInAs("nguoi-khac@example.com");

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverEmailMismatch, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
        string masked = "n" + new string('•', 8) + "n@example.com";
        Assert.Equal(masked, error.Extensions["invitedEmailMasked"]);
        Assert.DoesNotContain("nguoi-nhan", error.Message);
        Assert.Null(repository.Turns[0].ClaimantUserId);
        Assert.NotNull(other);
    }

    [Fact]
    public async Task Claim_ByToken_EmailDiffersOnlyByCaseAndSpaces_Succeeds()
    {
        claimant.Email = "nguoi-nhan@example.com";
        data.People[designation.BeneficiaryId].Email = " NGUOI-NHAN@Example.com ";
        AddTurn(HandoverTurnStatus.Invited, null);

        HandoverTurnResponse response = await service.ClaimAsync(Token, CancellationToken.None);

        Assert.Equal("IDENTITY_IN_PROGRESS", response.Status);
    }

    [Fact]
    public async Task Claim_RecipientEmailMissing_ThrowsEmailMismatchWithoutMaskedEmail()
    {
        data.People[designation.BeneficiaryId].Email = null;
        AddTurn(HandoverTurnStatus.Invited, null);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimAsync(Token, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverEmailMismatch, error.Code);
        Assert.False(error.Extensions.ContainsKey("invitedEmailMasked"));
    }

    [Fact]
    public async Task ClaimByTurn_RightEmail_ClaimsTurn()
    {
        policy.RequireIdentityVerification = true;
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);

        HandoverTurnResponse response = await service.ClaimByTurnAsync(turn.Id, CancellationToken.None);

        Assert.Equal("IDENTITY_IN_PROGRESS", response.Status);
        Assert.Equal(claimant.Id, turn.ClaimantUserId);
        Assert.Equal(Now, turn.ClaimedAt);
    }

    [Fact]
    public async Task ClaimByTurn_WrongEmail_ThrowsEmailMismatch()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        SignInAs("nguoi-khac@example.com");

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverEmailMismatch, error.Code);
        Assert.Null(turn.ClaimantUserId);
    }

    [Fact]
    public async Task ClaimByTurn_SameUserTwice_ReturnsCurrentStateWithoutWriting()
    {
        policy.RequireIdentityVerification = true;
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        await service.ClaimByTurnAsync(turn.Id, CancellationToken.None);
        int saves = unitOfWork.SaveCount;
        DateTimeOffset? claimedAt = turn.ClaimedAt;
        time.Now = Now.AddHours(2);

        HandoverTurnResponse second = await service.ClaimByTurnAsync(turn.Id, CancellationToken.None);

        Assert.Equal("IDENTITY_IN_PROGRESS", second.Status);
        Assert.Equal(claimedAt, turn.ClaimedAt);
        Assert.Equal(saves, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ClaimByTurn_UnknownTurn_ThrowsNotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task ClaimByTurn_OtherUserAlreadyClaimed_ThrowsAlreadyClaimed()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, Guid.NewGuid());

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverAlreadyClaimed, error.Code);
    }

    [Fact]
    public async Task ClaimByTurn_ByVaultOwner_ThrowsNotAllowed()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        currentUser.UserId = data.Owner.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("OWNER_CANNOT_CLAIM", error.Extensions["reason"]);
    }

    [Fact]
    public async Task ClaimByTurn_PlanNotInHandover_ThrowsNotAllowed()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        data.Plan.Status = PlanStatus.Closed;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal("PLAN_NOT_IN_HANDOVER", error.Extensions["reason"]);
    }

    [Fact]
    public async Task ClaimByTurn_TerminalTurn_ThrowsWrongState()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Cancelled, null);

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal("WRONG_STATE", error.Extensions["reason"]);
    }

    [Fact]
    public async Task ClaimByTurn_HoldActive_ThrowsNotAllowed()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        holdReader.Active = true;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal("HOLD_ACTIVE", error.Extensions["reason"]);
    }

    [Fact]
    public async Task ClaimByTurn_EmailNotVerified_ThrowsEmailNotVerified()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        claimant.EmailVerifiedAt = null;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ClaimByTurnAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.EmailNotVerified, error.Code);
    }

    [Fact]
    public async Task GetMyInvitations_RightEmail_ListsTurnWithOwnerAndContainer()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);

        List<PendingInvitationResponse> list = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(turn.Id, list[0].TurnId);
        Assert.Equal("Gói A", list[0].ContainerName);
        Assert.Equal("Chủ Kho", list[0].OwnerFullName);
        Assert.Equal(Now, list[0].InvitedAt);
    }

    [Fact]
    public async Task GetMyInvitations_ResponseExpiredTurn_StillListed()
    {
        AddTurn(HandoverTurnStatus.InitialResponseExpired, null);

        List<PendingInvitationResponse> list = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Single(list);
    }

    [Fact]
    public async Task GetMyInvitations_WrongEmail_ReturnsEmpty()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        SignInAs("nguoi-khac@example.com");

        List<PendingInvitationResponse> list = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Empty(list);
    }

    [Fact]
    public async Task GetMyInvitations_EmailNotVerified_ReturnsEmpty()
    {
        AddTurn(HandoverTurnStatus.Invited, null);
        claimant.EmailVerifiedAt = null;

        List<PendingInvitationResponse> list = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Empty(list);
    }

    [Fact]
    public async Task GetMyInvitations_AlreadyClaimedTurn_NotListed()
    {
        AddTurn(HandoverTurnStatus.IdentityInProgress, claimant.Id);

        List<PendingInvitationResponse> list = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Empty(list);
    }

    [Fact]
    public async Task GetMyInvitations_VaultOwner_DoesNotSeeOwnVaultTurn()
    {
        // Email người nhận trùng email Chủ kho: vẫn không được thấy lượt của kho mình.
        data.People[designation.BeneficiaryId].Email = data.Owner.Email;
        AddTurn(HandoverTurnStatus.Invited, null);
        currentUser.UserId = data.Owner.Id;

        List<PendingInvitationResponse> list = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Empty(list);
    }

    [Fact]
    public async Task GetMyInvitations_PlanNotInHandoverOrHoldActive_NotListed()
    {
        AddTurn(HandoverTurnStatus.Invited, null);

        holdReader.Active = true;
        List<PendingInvitationResponse> held = await service.GetMyInvitationsAsync(CancellationToken.None);
        holdReader.Active = false;
        data.Plan.Status = PlanStatus.Closed;
        List<PendingInvitationResponse> closed = await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Empty(held);
        Assert.Empty(closed);
    }

    [Fact]
    public async Task GetMyInvitations_DoesNotClaimAnything()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        int saves = unitOfWork.SaveCount;

        await service.GetMyInvitationsAsync(CancellationToken.None);

        Assert.Null(turn.ClaimantUserId);
        Assert.Equal(saves, unitOfWork.SaveCount);
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
