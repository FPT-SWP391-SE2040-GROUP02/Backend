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

/// <summary>Nối kết quả eKYC với lượt bàn giao: đạt và khớp thì sang READY_TO_ACCEPT (SRS §7.2, MATCH-01..05).</summary>
public sealed class HandoverIdentityTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeEkycVerificationRepository verifications = new FakeEkycVerificationRepository();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverIdentityService service;
    private readonly Guid claimantId = Guid.NewGuid();
    private readonly Container container;
    private readonly Designation designation;

    public HandoverIdentityTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = claimantId;
        container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        HandoverService handoverService = new HandoverService(
            currentUser, users, vaults, repository, new FakeTokenService(), new FakePolicy(), unitOfWork, time);
        service = new HandoverIdentityService(
            currentUser, vaults, repository, verifications, handoverService, holds, unitOfWork, time);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status, Guid? claimant)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, "t-" + Guid.NewGuid(), claimant);
        repository.Turns.Add(turn);
        return turn;
    }

    private EkycVerification AddVerification(
        HandoverTurn turn, EkycStatus status, EkycIdentityMatch match, EkycPurpose purpose = EkycPurpose.BeneficiaryClaim)
    {
        EkycVerification verification = new EkycVerification
        {
            UserId = claimantId,
            Purpose = purpose,
            HandoverTurnId = turn.Id,
            Status = status,
            IdentityMatch = match,
            CreatedAt = Now,
        };
        verifications.Verifications.Add(verification);
        return verification;
    }

    private async Task<string?> ReasonOfRejectionAsync(Guid turnId)
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteIdentityAsync(turnId, CancellationToken.None));
        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        return error.Extensions["reason"] as string;
    }

    [Fact]
    public async Task Complete_PassedAndMatched_MovesToReadyToAcceptWithDeadline()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched);

        HandoverTurnResponse response = await service.CompleteIdentityAsync(turn.Id, CancellationToken.None);

        Assert.Equal("READY_TO_ACCEPT", response.Status);
        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);
        Assert.Equal(Now, turn.ReadyAt);
        Assert.Equal(Now.AddDays(7), turn.DecisionDeadline);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Complete_CalledAgain_KeepsTheSameDeadlineAndDoesNotSave()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched);
        await service.CompleteIdentityAsync(turn.Id, CancellationToken.None);
        time.Now = Now.AddDays(1);

        HandoverTurnResponse response = await service.CompleteIdentityAsync(turn.Id, CancellationToken.None);

        Assert.Equal("READY_TO_ACCEPT", response.Status);
        Assert.Equal(Now.AddDays(7), turn.DecisionDeadline);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Complete_ActiveHold_RejectedEvenWhenEkycPassed()
    {
        // M5, ACCEPT-01: kho đang bị giữ thì chưa sang "sẵn sàng nhận", dù eKYC đã đạt
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched);
        holds.Active = true;

        Assert.Equal("HOLD_ACTIVE", await ReasonOfRejectionAsync(turn.Id));
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
        Assert.Null(turn.DecisionDeadline);
    }

    [Fact]
    public async Task Complete_NoEkycForThisTurn_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);

        Assert.Equal("IDENTITY_NOT_VERIFIED", await ReasonOfRejectionAsync(turn.Id));
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Complete_EkycOfAnotherTurn_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        HandoverTurn other = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(other, EkycStatus.Passed, EkycIdentityMatch.Matched);

        Assert.Equal("IDENTITY_NOT_VERIFIED", await ReasonOfRejectionAsync(turn.Id));
    }

    [Theory]
    [InlineData(EkycStatus.Pending, EkycIdentityMatch.Matched, "IDENTITY_NOT_FINISHED")]
    [InlineData(EkycStatus.Failed, EkycIdentityMatch.Matched, "IDENTITY_FAILED")]
    [InlineData(EkycStatus.Passed, EkycIdentityMatch.WrongPerson, "IDENTITY_WRONG_PERSON")]
    [InlineData(EkycStatus.Passed, EkycIdentityMatch.NeedsReview, "IDENTITY_NEEDS_REVIEW")]
    [InlineData(EkycStatus.Passed, EkycIdentityMatch.NotChecked, "IDENTITY_NOT_VERIFIED")]
    public async Task Complete_NotGoodEnough_RejectedAndTurnUntouched(
        EkycStatus status, EkycIdentityMatch match, string expectedReason)
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(turn, status, match);

        Assert.Equal(expectedReason, await ReasonOfRejectionAsync(turn.Id));
        // Chỉ chặn, không tự đóng lượt khi sai người (việc đóng lượt không thuộc bước này).
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
        Assert.Null(turn.ReadyAt);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Complete_OwnerPurposeEkyc_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched, EkycPurpose.OwnerEnrollment);

        Assert.Equal("IDENTITY_NOT_VERIFIED", await ReasonOfRejectionAsync(turn.Id));
    }

    [Fact]
    public async Task Complete_UsesTheLatestEkycOfTheTurn()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        EkycVerification wrong = AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.WrongPerson);
        wrong.CreatedAt = Now.AddMinutes(-10);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched);

        HandoverTurnResponse response = await service.CompleteIdentityAsync(turn.Id, CancellationToken.None);

        Assert.Equal("READY_TO_ACCEPT", response.Status);
    }

    [Fact]
    public async Task Complete_OtherUsersTurn_NotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, Guid.NewGuid());

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteIdentityAsync(turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Fact]
    public async Task Complete_UnknownTurn_NotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.CompleteIdentityAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }

    [Theory]
    [InlineData(HandoverTurnStatus.Invited)]
    [InlineData(HandoverTurnStatus.HandoverCommitted)]
    [InlineData(HandoverTurnStatus.Declined)]
    public async Task Complete_TurnNotInIdentityStep_WrongState(HandoverTurnStatus status)
    {
        HandoverTurn turn = AddTurn(status, claimantId);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched);

        Assert.Equal("WRONG_STATE", await ReasonOfRejectionAsync(turn.Id));
        Assert.Equal(status, turn.Status);
    }

    [Fact]
    public async Task Complete_PlanNoLongerInHandover_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, claimantId);
        AddVerification(turn, EkycStatus.Passed, EkycIdentityMatch.Matched);
        data.Plan.Status = PlanStatus.Blocked;

        Assert.Equal("PLAN_NOT_IN_HANDOVER", await ReasonOfRejectionAsync(turn.Id));
    }
}
