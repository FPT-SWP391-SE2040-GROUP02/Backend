using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Người nhận xác nhận đã nhận đủ và thấy Người xét duyệt đã duyệt hồ sơ.</summary>
public sealed class HandoverReceivedAllTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeHandoverSubmissionRepository submissions = new FakeHandoverSubmissionRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly Guid claimantId = Guid.NewGuid();
    private readonly Container container;
    private readonly Designation designation;
    private readonly HandoverService service;

    public HandoverReceivedAllTests()
    {
        FakeVaultRepository vaults = new FakeVaultRepository();
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = claimantId;
        container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        service = new HandoverService(currentUser, new FakeUserRepository(), vaults, repository, new FakeTokenService(),
            new FakePolicy(), new NoHoldReader(), unitOfWork, new FixedTimeProvider(Now), null, submissions);
    }

    private HandoverTurn AddTurn(HandoverTurnStatus status)
    {
        HandoverTurn turn = data.BuildTurn(container, designation, status, "tk", claimantId);
        repository.Turns.Add(turn);
        if (status == HandoverTurnStatus.HandoverCommitted)
        {
            HandoverGrant grant = new HandoverGrant
            {
                TurnId = turn.Id, Turn = turn, UserId = claimantId, Status = HandoverGrantStatus.Active,
                GrantedAt = Now, ExpiresAt = Now.AddDays(30)
            };
            turn.Grant = grant;
            repository.Grants.Add(grant);
        }
        return turn;
    }

    [Fact]
    public async Task ConfirmReceivedAll_AfterCommit_StoresTimeAndIsIdempotent()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.HandoverCommitted);

        HandoverTurnResponse first = await service.ConfirmReceivedAllAsync(turn.Id, CancellationToken.None);
        int saves = unitOfWork.SaveCount;
        HandoverTurnResponse second = await service.ConfirmReceivedAllAsync(turn.Id, CancellationToken.None);

        Assert.Equal(Now, first.ReceivedAllAt);
        Assert.Equal(Now, second.ReceivedAllAt);
        Assert.Equal(saves, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task ConfirmReceivedAll_BeforeCommit_IsRejected()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept);

        AppException ex = await Assert.ThrowsAsync<AppException>(
            () => service.ConfirmReceivedAllAsync(turn.Id, CancellationToken.None));

        Assert.Equal(AppErrorType.Conflict, ex.Type);
        Assert.Null(turn.ReceivedAllAt);
    }

    [Fact]
    public async Task GetTurn_ShowsApprovingVerifierNameAndLawyerCard()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.ReadyToAccept);
        submissions.Reviewers[turn.VaultId] = ("Luật sư Dũng", "LS-12345");

        HandoverTurnResponse response = await service.GetTurnAsync(turn.Id, CancellationToken.None);

        Assert.Equal("Luật sư Dũng", response.Reviewer!.FullName);
        Assert.Equal("LS-12345", response.Reviewer.LawyerCardNumber);
    }
}
