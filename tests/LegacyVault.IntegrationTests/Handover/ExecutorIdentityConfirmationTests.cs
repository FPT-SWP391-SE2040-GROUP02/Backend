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

/// <summary>Executor xác nhận đúng người bằng tay thay cho eKYC (BR-122). Dùng fake, không cần SQL.</summary>
public sealed class ExecutorIdentityConfirmationTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeProgressExecutorReader executors = new FakeProgressExecutorReader();
    private readonly FakeHandoverProgressReader reader = new FakeHandoverProgressReader();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly ExecutorIdentityConfirmationService service;
    private readonly Guid executorUserId = Guid.NewGuid();
    private readonly Guid recipientUserId = Guid.NewGuid();
    private readonly DeathCase deathCase;
    private readonly Container container;
    private readonly Designation designation;

    public ExecutorIdentityConfirmationTests()
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

        deathCase = new DeathCase();
        deathCase.VaultId = data.Vault.Id;
        deathCase.PlanId = data.Plan.Id;
        deathCase.Status = DeathCaseStatus.ApprovedForHandover;
        reader.Cases.Add(deathCase);

        service = new ExecutorIdentityConfirmationService(
            currentUser, vaults, executors, reader, repository, holds, unitOfWork, time);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status, Guid? claimant)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, "token", claimant);
        repository.Turns.Add(turn);
        return turn;
    }

    private async Task<string?> ReasonOfRejectionAsync(Guid turnId)
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ConfirmAsync(deathCase.Id, turnId, CancellationToken.None));
        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        return error.Extensions["reason"] as string;
    }

    [Fact]
    public async Task Confirm_WaitingForIdentity_BecomesReadyAndStartsDecisionClock()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, recipientUserId);

        ConfirmHandoverIdentityResponse result = await service.ConfirmAsync(deathCase.Id, turn.Id, CancellationToken.None);

        Assert.Equal(HandoverTurnStatus.ReadyToAccept, turn.Status);
        Assert.Equal(executorUserId, turn.IdentityConfirmedByUserId);
        Assert.Equal(Now, turn.IdentityConfirmedAt);
        Assert.Equal(Now.AddDays(7), turn.DecisionDeadline);
        Assert.Equal(Now.AddDays(7), result.DecisionDeadline);
        Assert.Equal("READY_TO_ACCEPT", result.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Confirm_PressedTwice_ReturnsSameResultWithoutResettingDeadline()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, recipientUserId);
        await service.ConfirmAsync(deathCase.Id, turn.Id, CancellationToken.None);
        DateTimeOffset firstDeadline = turn.DecisionDeadline!.Value;

        time.Now = Now.AddDays(1);
        ConfirmHandoverIdentityResponse again = await service.ConfirmAsync(deathCase.Id, turn.Id, CancellationToken.None);

        Assert.Equal(firstDeadline, again.DecisionDeadline);
        Assert.Equal(firstDeadline, turn.DecisionDeadline);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Confirm_OwnTurn_Rejected()
    {
        // Executor kiêm người nhận không tự xác nhận cho mình được
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, executorUserId);

        Assert.Equal("OWN_TURN", await ReasonOfRejectionAsync(turn.Id));
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
        Assert.Null(turn.IdentityConfirmedByUserId);
    }

    [Theory]
    [InlineData(HandoverTurnStatus.Invited)]
    [InlineData(HandoverTurnStatus.InitialResponseExpired)]
    [InlineData(HandoverTurnStatus.HandoverCommitted)]
    [InlineData(HandoverTurnStatus.Declined)]
    public async Task Confirm_TurnNotWaitingForIdentity_Rejected(HandoverTurnStatus status)
    {
        Guid? claimant = null;
        if (status != HandoverTurnStatus.Invited && status != HandoverTurnStatus.InitialResponseExpired)
        {
            claimant = recipientUserId;
        }
        HandoverTurn turn = AddTurn(status, claimant);

        Assert.Equal("WRONG_STATE", await ReasonOfRejectionAsync(turn.Id));
        Assert.Null(turn.IdentityConfirmedByUserId);
    }

    [Fact]
    public async Task Confirm_VaultOnHold_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, recipientUserId);
        holds.Active = true;

        Assert.Equal("HOLD_ACTIVE", await ReasonOfRejectionAsync(turn.Id));
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Confirm_PlanNotInHandover_Rejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, recipientUserId);
        data.Vault.Plan.Status = PlanStatus.Closed;

        Assert.Equal("PLAN_NOT_IN_HANDOVER", await ReasonOfRejectionAsync(turn.Id));
    }

    [Fact]
    public async Task Confirm_CallerIsNotActiveExecutor_NotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, recipientUserId);
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ConfirmAsync(deathCase.Id, turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
        Assert.Equal(HandoverTurnStatus.IdentityInProgress, turn.Status);
    }

    [Fact]
    public async Task Confirm_OwnerCalling_NotFound()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.IdentityInProgress, recipientUserId);
        currentUser.UserId = data.Vault.OwnerId;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ConfirmAsync(deathCase.Id, turn.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.DeathCaseNotFound, error.Code);
    }

    [Fact]
    public async Task Confirm_UnknownTurn_NotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.ConfirmAsync(deathCase.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
    }
}
