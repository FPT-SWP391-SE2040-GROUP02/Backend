using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.Ekyc;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Executor gửi lại lời mời bàn giao (H2, INVITE-02, INVITE-04). Dùng fake, không cần SQL.</summary>
public sealed class HandoverInvitationServiceTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeProgressExecutorReader executors = new FakeProgressExecutorReader();
    private readonly FakeHandoverProgressReader reader = new FakeHandoverProgressReader();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeInvitationSender sender = new FakeInvitationSender();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverInvitationService service;
    private readonly Guid executorUserId = Guid.NewGuid();
    private readonly DeathCase deathCase;
    private readonly Container container;
    private readonly Designation designation;

    public HandoverInvitationServiceTests()
    {
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = executorUserId;

        Executor executor = new Executor();
        executor.VaultId = data.Vault.Id;
        executor.UserId = executorUserId;
        executor.Status = ExecutorStatus.Accepted;
        executors.Executors.Add(executor);

        container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        protector.Data[designation.BeneficiaryId] = data.People[designation.BeneficiaryId];

        deathCase = new DeathCase();
        deathCase.VaultId = data.Vault.Id;
        deathCase.PlanId = data.Plan.Id;
        deathCase.Status = DeathCaseStatus.ApprovedForHandover;
        reader.Cases.Add(deathCase);

        service = new HandoverInvitationService(
            currentUser, vaults, executors, reader, repository, beneficiaries, protector,
            new FakeTokenService(), sender, holds, unitOfWork, time);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status, DateTimeOffset? sentAt, Guid? claimant = null)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, "old-token", claimant);
        turn.InvitationSentAt = sentAt;
        repository.Turns.Add(turn);
        return turn;
    }

    private async Task<string?> ReasonOfRejectionAsync(Guid turnId)
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ResendAsync(deathCase.Id, turnId, CancellationToken.None));
        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        return error.Extensions["reason"] as string;
    }

    [Fact]
    public async Task Resend_AfterFirstDelivery_NewTokenSameDeadline()
    {
        // Lời mời đã giao 1 ngày trước; gửi lại không đặt lại hạn (INVITE-02)
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddDays(-1));
        DateTimeOffset deadline = turn.ResponseDeadline;
        string oldHash = turn.InvitationTokenHash;

        ResendHandoverInvitationResponse result = await service.ResendAsync(deathCase.Id, turn.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("nguoi-nhan@example.com", sender.Sent[0].ToEmail);
        Assert.Equal(deadline, turn.ResponseDeadline);
        Assert.Equal(deadline, result.ResponseDeadline);
        Assert.Equal(Now, turn.InvitationSentAt);
        Assert.NotEqual(oldHash, turn.InvitationTokenHash);
        Assert.Equal(new FakeTokenService().HashOpaqueToken(sender.Sent[0].Token), turn.InvitationTokenHash);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Resend_NeverDelivered_StartsFiveDayClockFromDelivery()
    {
        // INVITE-04: chưa giao được thì chưa chạy hạn; giao lần đầu thì hạn tính từ bây giờ
        HandoverTurn turn = AddTurn(HandoverTurnStatus.InitialResponseExpired, null);
        turn.ResponseDeadline = Now.AddDays(-2);

        ResendHandoverInvitationResponse result = await service.ResendAsync(deathCase.Id, turn.Id, CancellationToken.None);

        Assert.Equal(Now.AddDays(5), turn.ResponseDeadline);
        Assert.Equal(Now.AddDays(5), result.ResponseDeadline);
        Assert.Equal(HandoverTurnStatus.Invited, turn.Status);
        Assert.Equal(Now, turn.InvitationSentAt);
    }

    [Fact]
    public async Task Resend_SendFailure_ChangesNothingAndKeepsOldLinkValid()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);
        string oldHash = turn.InvitationTokenHash;
        sender.FailNext = true;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ResendAsync(deathCase.Id, turn.Id, CancellationToken.None));

        Assert.Equal(AppErrorType.ServiceUnavailable, error.Type);
        Assert.Equal(oldHash, turn.InvitationTokenHash);
        Assert.Null(turn.InvitationSentAt);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Resend_RecipientWithoutEmail_Rejected()
    {
        protector.Data[designation.BeneficiaryId] = new BeneficiaryPersonalData
        {
            CitizenId = "012345671234", FullName = "Người Nhận", DateOfBirth = new DateOnly(1990, 1, 1), Email = null
        };
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, null);

        Assert.Equal("NO_EMAIL", await ReasonOfRejectionAsync(turn.Id));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Resend_TooSoonAfterLastSend_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddMinutes(-5));

        Assert.Equal("RESEND_TOO_SOON", await ReasonOfRejectionAsync(turn.Id));
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Resend_AlreadyClaimed_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, Now.AddDays(-1), Guid.NewGuid());

        Assert.Equal("WRONG_STATE", await ReasonOfRejectionAsync(turn.Id));
    }

    [Fact]
    public async Task Resend_ActiveHold_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddDays(-1));
        holds.Active = true;

        Assert.Equal("HOLD_ACTIVE", await ReasonOfRejectionAsync(turn.Id));
    }

    [Fact]
    public async Task Resend_PlanNotInHandover_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddDays(-1));
        data.Plan.Status = PlanStatus.Blocked;

        Assert.Equal("PLAN_NOT_IN_HANDOVER", await ReasonOfRejectionAsync(turn.Id));
    }

    [Fact]
    public async Task Resend_NotAnActiveExecutor_NotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddDays(-1));
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ResendAsync(deathCase.Id, turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Resend_OwnerCannotCall_NotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddDays(-1));
        currentUser.UserId = data.Owner.Id;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ResendAsync(deathCase.Id, turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
    }

    [Fact]
    public async Task Resend_TurnOfAnotherPlan_NotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.Invited, Now.AddDays(-1));
        turn.PlanId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ResendAsync(deathCase.Id, turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }
}
