using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
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
            new FakePolicy(), unitOfWork, new FixedTimeProvider(Now), new HandoverKeyTests.FakeKeyShards(), submissions,
            new LegacyVault.IntegrationTests.Ekyc.FakeEkycBeneficiaryRepository(), new FakeBeneficiaryDataProtector());
    }

    private HandoverService BuildServiceWith(IHandoverProgressReader reader)
    {
        FakeVaultRepository vaults = new FakeVaultRepository();
        vaults.Vaults.Add(data.Vault);
        return new HandoverService(currentUser, new FakeUserRepository(), vaults, repository, new FakeTokenService(),
            new FakePolicy(), unitOfWork, new FixedTimeProvider(Now), new HandoverKeyTests.FakeKeyShards(), submissions,
            new LegacyVault.IntegrationTests.Ekyc.FakeEkycBeneficiaryRepository(), new FakeBeneficiaryDataProtector(),
            null, reader);
    }

    private static HandoverSnapshotItem AddItem(HandoverTurn turn, string name, int position)
    {
        HandoverSnapshotItem item = new HandoverSnapshotItem
        {
            TurnId = turn.Id, Turn = turn, AssetId = Guid.NewGuid(), Name = name, Kind = AssetKind.Text,
            ContentType = "text/plain", SizeBytes = 3, Position = position
        };
        turn.Items.Add(item);
        return item;
    }

    /// <summary>Mục nào đã tải (khóa là id mục đã ghim); các hàm khác không dùng trong test này.</summary>
    private sealed class FakeDownloads : IHandoverProgressReader
    {
        public Dictionary<Guid, DateTimeOffset> Downloaded { get; } = new Dictionary<Guid, DateTimeOffset>();

        public Task<Dictionary<Guid, DateTimeOffset>> FindFirstDownloadTimesAsync(List<Guid> grantIds, CancellationToken ct)
        {
            return Task.FromResult(new Dictionary<Guid, DateTimeOffset>(Downloaded));
        }

        public Task<HandoverRecord?> FindRecordAsync(Guid recordId, CancellationToken ct) => Task.FromResult<HandoverRecord?>(null);

        public Task<List<HandoverTurn>> FindTurnsWithItemsAsync(Guid planId, CancellationToken ct) => Task.FromResult(new List<HandoverTurn>());

        public Task<Dictionary<Guid, string>> FindCitizenIdsAsync(List<Guid> beneficiaryIds, CancellationToken ct) =>
            Task.FromResult(new Dictionary<Guid, string>());
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
    public async Task ConfirmReceivedAll_WithUndownloadedItem_IsRejectedUntilAllDownloaded()
    {
        HandoverTurn turn = AddTurn(HandoverTurnStatus.HandoverCommitted);
        HandoverSnapshotItem first = AddItem(turn, "Mục A", 0);
        HandoverSnapshotItem second = AddItem(turn, "Mục B", 1);
        FakeDownloads downloads = new FakeDownloads();
        HandoverService strict = BuildServiceWith(downloads);

        // Mới tải một mục: chưa xác nhận được
        downloads.Downloaded[first.Id] = Now;
        AppException ex = await Assert.ThrowsAsync<AppException>(
            () => strict.ConfirmReceivedAllAsync(turn.Id, CancellationToken.None));
        Assert.Equal(AppErrorType.Conflict, ex.Type);
        Assert.Null(turn.ReceivedAllAt);

        // Tải đủ cả hai: xác nhận được, và màn hình báo từng mục đã tải
        foreach (HandoverSnapshotItem item in turn.Items)
        {
            downloads.Downloaded[item.Id] = Now;
        }
        Assert.Contains(second, turn.Items);
        HandoverTurnResponse response = await strict.ConfirmReceivedAllAsync(turn.Id, CancellationToken.None);
        Assert.Equal(Now, response.ReceivedAllAt);
        Assert.All(response.Items, item => Assert.True(item.Downloaded));
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
