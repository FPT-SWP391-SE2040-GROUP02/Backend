using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Mở bàn giao: mỗi chỉ định một lượt, ghim snapshot, gửi lời mời sau khi lưu.</summary>
public sealed class HandoverStarterTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeTokenService tokens = new FakeTokenService();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FakeInvitationSender sender;
    private readonly HandoverStarter starter;

    public HandoverStarterTests()
    {
        sender = new FakeInvitationSender(unitOfWork);
        starter = new HandoverStarter(vaults, repository, protector, tokens, sender, unitOfWork, new FixedTimeProvider(Now));
        vaults.Vaults.Add(data.Vault);
    }

    private void Publish()
    {
        repository.Designations.AddRange(data.Designations);
        foreach (KeyValuePair<Guid, LegacyVault.Application.Security.BeneficiaryPersonalData> pair in data.People)
        {
            protector.Data[pair.Key] = pair.Value;
        }
    }

    [Fact]
    public async Task Start_CreatesOneTurnPerDesignationWithPinnedSnapshot()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", "a@example.com");
        data.Designate(container, "Người B", "b@example.com");
        Publish();

        HandoverStartResult result = await starter.StartAsync(data.Vault.Id, CancellationToken.None);

        Assert.Equal(2, result.TurnCount);
        Assert.Equal(PlanStatus.HandoverInProgress, data.Plan.Status);
        Assert.Equal(2, repository.Turns.Count);

        for (int i = 0; i < repository.Turns.Count; i++)
        {
            HandoverTurn turn = repository.Turns[i];
            Assert.Equal(HandoverTurnStatus.Invited, turn.Status);
            Assert.Equal(Now, turn.InvitedAt);
            Assert.Equal(Now.AddDays(5), turn.ResponseDeadline);
            Assert.Equal("Gói A", turn.ContainerName);
            Assert.Equal(2, turn.Items.Count);
            Assert.Equal(container.Assets[0].Asset.Id, turn.Items[0].AssetId);
            Assert.Equal(container.Assets[0].Asset.Name, turn.Items[0].Name);
            Assert.Equal(1000, turn.Items[0].SizeBytes);

            string rawToken = sender.Sent[i].Token;
            Assert.NotEqual(rawToken, turn.InvitationTokenHash);
            Assert.Equal(tokens.HashOpaqueToken(rawToken), turn.InvitationTokenHash);
        }
    }

    [Fact]
    public async Task Start_IgnoresRemovedAndDraftContainers()
    {
        Container ready = data.AddContainer("Sẵn sàng", ContainerStatus.Ready, 1);
        Container draft = data.AddContainer("Nháp", ContainerStatus.Draft, 1);
        Container removedContainer = data.AddContainer("Đã xóa", ContainerStatus.Removed, 1);
        data.Designate(ready, "Người A", "a@example.com");
        data.Designate(draft, "Người B", "b@example.com");
        data.Designate(removedContainer, "Người C", "c@example.com");
        Publish();

        HandoverStartResult result = await starter.StartAsync(data.Vault.Id, CancellationToken.None);

        Assert.Equal(1, result.TurnCount);
        Assert.Equal("Sẵn sàng", repository.Turns[0].ContainerName);
    }

    [Theory]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.Active)]
    [InlineData(PlanStatus.CheckinOverdue)]
    [InlineData(PlanStatus.AwaitingCheck)]
    [InlineData(PlanStatus.Closed)]
    [InlineData(PlanStatus.Blocked)]
    public async Task Start_RequiresDeathCaseOpen(PlanStatus status)
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", "a@example.com");
        Publish();
        data.Plan.Status = status;

        AppException error = await Assert.ThrowsAsync<AppException>(() => starter.StartAsync(data.Vault.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal("PLAN_NOT_READY", error.Extensions["reason"]);
        Assert.Empty(repository.Turns);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Start_CalledTwice_CreatesNothingNew()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", "a@example.com");
        Publish();

        await starter.StartAsync(data.Vault.Id, CancellationToken.None);
        int sentBefore = sender.Sent.Count;
        HandoverStartResult second = await starter.StartAsync(data.Vault.Id, CancellationToken.None);

        Assert.True(second.AlreadyStarted);
        Assert.Equal(1, second.TurnCount);
        Assert.Single(repository.Turns);
        Assert.Equal(sentBefore, sender.Sent.Count);
    }

    [Fact]
    public async Task Start_SendsInvitationsOnlyAfterSaving()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Nguyễn Văn A", "a@example.com");
        Publish();

        await starter.StartAsync(data.Vault.Id, CancellationToken.None);

        Assert.Single(sender.Sent);
        Assert.True(sender.SaveCountsAtSend[0] >= 1);
        Assert.Equal("a@example.com", sender.Sent[0].ToEmail);
        Assert.Equal("Nguyễn Văn A", sender.Sent[0].FullName);
        Assert.Equal(repository.Turns[0].ResponseDeadline, sender.Sent[0].ResponseDeadline);
        Assert.Equal(repository.Turns[0].Id, sender.Sent[0].TurnId);
    }

    [Fact]
    public async Task Start_SenderFailure_KeepsTurnsAndLeavesInvitationSentAtNull()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", "a@example.com");
        data.Designate(container, "Người B", "b@example.com");
        Publish();
        sender.FailNext = true;

        HandoverStartResult result = await starter.StartAsync(data.Vault.Id, CancellationToken.None);

        Assert.Equal(2, repository.Turns.Count);
        Assert.Equal(1, result.InvitationsFailed);
        Assert.Equal(1, result.InvitationsSent);
        Assert.Null(repository.Turns[0].InvitationSentAt);
        Assert.Equal(Now, repository.Turns[1].InvitationSentAt);
    }

    [Fact]
    public async Task Start_BeneficiaryWithoutEmail_CreatesTurnButSendsNothing()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", null);
        Publish();

        HandoverStartResult result = await starter.StartAsync(data.Vault.Id, CancellationToken.None);

        Assert.Equal(1, result.TurnCount);
        Assert.Empty(sender.Sent);
        Assert.Null(repository.Turns[0].InvitationSentAt);
    }

    [Fact]
    public async Task Start_NoStartableDesignations_ThrowsHandoverNotAllowed()
    {
        data.AddContainer("Gói trống", ContainerStatus.Ready, 1);
        Publish();

        AppException error = await Assert.ThrowsAsync<AppException>(() => starter.StartAsync(data.Vault.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("NOTHING_TO_HAND_OVER", error.Extensions["reason"]);
    }
}
