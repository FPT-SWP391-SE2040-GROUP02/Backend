using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.Ekyc;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Job gửi lại lời mời F3 chưa giao được (OPS-04, INVITE-04). Dùng fake, không cần SQL.
/// Thời gian chạy ở 10 phút sau khi lượt được tạo để lượt đã đủ tuổi (MinAge).
/// </summary>
public sealed class HandoverInvitationRetryJobTests
{
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(CreatedAt);
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeEkycBeneficiaryRepository beneficiaries = new FakeEkycBeneficiaryRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeInvitationSender sender = new FakeInvitationSender();
    private readonly FakeHoldReader holds = new FakeHoldReader();
    private readonly FakeIncidentService incidents = new FakeIncidentService();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(CreatedAt.AddMinutes(10));
    private readonly HandoverInvitationRetryJob job;
    private readonly Container container;
    private readonly Designation designation;

    public HandoverInvitationRetryJobTests()
    {
        data.Plan.Status = PlanStatus.HandoverInProgress;
        vaults.Vaults.Add(data.Vault);

        container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        beneficiaries.Beneficiaries.Add(designation.Beneficiary);
        protector.Data[designation.BeneficiaryId] = data.People[designation.BeneficiaryId];

        job = new HandoverInvitationRetryJob(
            vaults, repository, beneficiaries, protector, new FakeTokenService(), sender, holds, incidents,
            unitOfWork, time);
    }

    private HandoverTurn AddUndeliveredTurn()
    {
        HandoverTurn turn = data.BuildTurn(container, designation, HandoverTurnStatus.Invited, "old-token", null);
        turn.InvitationSentAt = null;
        repository.Turns.Add(turn);
        return turn;
    }

    [Fact]
    public async Task Finds_OnlyUndeliveredOldEnoughTurns()
    {
        HandoverTurn undelivered = AddUndeliveredTurn();
        HandoverTurn delivered = AddUndeliveredTurn();
        delivered.InvitationSentAt = CreatedAt.AddMinutes(1);
        HandoverTurn tooYoung = AddUndeliveredTurn();
        tooYoung.CreatedAt = CreatedAt.AddMinutes(9);
        HandoverTurn exhausted = AddUndeliveredTurn();
        exhausted.InvitationAttempts = HandoverInvitationRetryJob.MaxAttempts;

        IReadOnlyList<Guid> ids = await job.FindPendingTurnIdsAsync(CancellationToken.None);

        Assert.Single(ids);
        Assert.Equal(undelivered.Id, ids[0]);
    }

    [Fact]
    public async Task Process_SendsNewTokenAndStartsDeadlineFromDelivery()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        string oldHash = turn.InvitationTokenHash;

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.Equal("nguoi-nhan@example.com", sender.Sent[0].ToEmail);
        Assert.Equal(time.GetUtcNow(), turn.InvitationSentAt);
        Assert.Equal(time.GetUtcNow().AddDays(5), turn.ResponseDeadline);
        Assert.NotEqual(oldHash, turn.InvitationTokenHash);
        Assert.Equal(new FakeTokenService().HashOpaqueToken(sender.Sent[0].Token), turn.InvitationTokenHash);
        Assert.Empty(incidents.Raised);
    }

    [Fact]
    public async Task Process_IsIdempotentAfterDelivery()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Process_SendFailure_CountsAttemptKeepsOldLinkAndBacksOff()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        string oldHash = turn.InvitationTokenHash;
        sender.FailNext = true;

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Equal(1, turn.InvitationAttempts);
        Assert.Equal(time.GetUtcNow(), turn.InvitationLastAttemptAt);
        Assert.Null(turn.InvitationSentAt);
        Assert.Equal(oldHash, turn.InvitationTokenHash);
        Assert.Empty(incidents.Raised);

        // Chưa đủ giãn cách (5 phút) thì chưa thử lại.
        time.Now = time.Now.AddMinutes(4);
        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);
        Assert.Empty(sender.Sent);
        Assert.Equal(1, turn.InvitationAttempts);

        // Đủ giãn cách thì gửi được.
        time.Now = time.Now.AddMinutes(2);
        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);
        Assert.Single(sender.Sent);
        Assert.NotNull(turn.InvitationSentAt);
    }

    [Fact]
    public async Task Process_FifthFailure_RaisesNotificationFailedIncident()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        turn.InvitationAttempts = HandoverInvitationRetryJob.MaxAttempts - 1;
        turn.InvitationLastAttemptAt = CreatedAt;
        // Lần thử thứ 5 phải chờ 4 x 5 phút kể từ lần lỗi trước.
        time.Now = CreatedAt.AddMinutes(30);
        sender.FailNext = true;

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Equal(HandoverInvitationRetryJob.MaxAttempts, turn.InvitationAttempts);
        Assert.Single(incidents.Raised);
        Assert.Equal(IncidentType.NotificationFailed, incidents.Raised[0].Type);
        Assert.Equal(HandoverInvitationRetryJob.TargetTurn, incidents.Raised[0].TargetType);
        Assert.Equal(turn.Id, incidents.Raised[0].TargetId);

        // Đã ngừng thử.
        IReadOnlyList<Guid> ids = await job.FindPendingTurnIdsAsync(CancellationToken.None);
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Process_RecipientWithoutEmail_StopsAndRaisesIncident()
    {
        protector.Data[designation.BeneficiaryId] = new BeneficiaryPersonalData
        {
            CitizenId = "012345671234", FullName = "Người Nhận", DateOfBirth = new DateOnly(1990, 1, 1), Email = null
        };
        HandoverTurn turn = AddUndeliveredTurn();

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Equal(HandoverInvitationRetryJob.MaxAttempts, turn.InvitationAttempts);
        Assert.Single(incidents.Raised);
    }

    [Fact]
    public async Task Process_ActiveHold_PausesWithoutCountingFailure()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        holds.Active = true;

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Equal(0, turn.InvitationAttempts);
        Assert.Empty(incidents.Raised);
    }

    [Fact]
    public async Task Process_PlanNoLongerInHandover_DoesNothing()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        data.Plan.Status = PlanStatus.Closed;

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
        Assert.Null(turn.InvitationSentAt);
    }

    [Fact]
    public async Task Process_ClaimedTurn_IsSkipped()
    {
        HandoverTurn turn = AddUndeliveredTurn();
        turn.ClaimantUserId = Guid.NewGuid();

        await job.ProcessTurnAsync(turn.Id, CancellationToken.None);

        Assert.Empty(sender.Sent);
    }
}
