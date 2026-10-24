using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Tải tài sản: kiểm toàn vẹn bằng một lượt giải mã vào Stream.Null trước khi gửi byte nào (DOWNLOAD-02..05).</summary>
public sealed class HandoverDownloadTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private const int ContentLength = 200000;

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeAssetContentStore store = new FakeAssetContentStore();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider time = new FixedTimeProvider(Now);
    private readonly HandoverDownloadService service;
    private readonly Guid claimantId = Guid.NewGuid();
    private readonly byte[] content = new byte[ContentLength];
    private readonly HandoverTurn turn;
    private readonly HandoverGrant grant;
    private readonly HandoverSnapshotItem item;

    public HandoverDownloadTests()
    {
        Random.Shared.NextBytes(content);
        vaults.Vaults.Add(data.Vault);
        currentUser.UserId = claimantId;

        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        Designation designation = data.Designate(container, "Người Nhận", "nguoi-nhan@example.com");
        turn = data.BuildTurn(container, designation, HandoverTurnStatus.HandoverCommitted, "t1", claimantId);
        repository.Turns.Add(turn);

        item = turn.Items[0];
        item.SizeBytes = ContentLength;
        Asset asset = data.Assets[0];
        asset.SizeBytes = ContentLength;
        repository.Assets.Add(asset);
        store.Contents[asset.StorageKey] = content;

        grant = new HandoverGrant
        {
            TurnId = turn.Id,
            Turn = turn,
            UserId = claimantId,
            Status = HandoverGrantStatus.Active,
            GrantedAt = Now,
            ExpiresAt = Now.AddDays(30)
        };
        turn.Grant = grant;
        repository.Grants.Add(grant);

        service = new HandoverDownloadService(currentUser, vaults, repository, store, unitOfWork, time);
    }

    [Fact]
    public async Task Prepare_ReturnsMetadataAndStreamsExactBytes()
    {
        PreparedDownload download = await service.PrepareAsync(grant.Id, item.Id, CancellationToken.None);

        Assert.Equal(item.FileName, download.FileName);
        Assert.Equal(item.ContentType, download.ContentType);
        Assert.Equal(ContentLength, download.Length);

        using MemoryStream output = new MemoryStream();
        await download.WriteToAsync(output, CancellationToken.None);

        Assert.Equal(content, output.ToArray());
        Assert.Equal(2, store.Calls.Count);
        Assert.True(store.Calls[0].DestinationIsNull);
        Assert.False(store.Calls[1].DestinationIsNull);
    }

    [Fact]
    public async Task Prepare_VerificationPass_UsesNullStream_NotMemory()
    {
        await service.PrepareAsync(grant.Id, item.Id, CancellationToken.None);

        Assert.Single(store.Calls);
        Assert.True(store.Calls[0].DestinationIsNull);
    }

    [Fact]
    public async Task Prepare_CorruptedContent_ThrowsIntegrityAndNeverWritesToResponse()
    {
        store.CorruptAfterBytes = 150000;

        await Assert.ThrowsAsync<ContentIntegrityException>(() => service.PrepareAsync(grant.Id, item.Id, CancellationToken.None));

        Assert.All(store.Calls, call => Assert.True(call.DestinationIsNull));
        Assert.Single(repository.Logs);
        Assert.Equal(HandoverAccessOutcome.IntegrityFailed, repository.Logs[0].Outcome);
        Assert.True(unitOfWork.SaveCount >= 1);
    }

    [Fact]
    public async Task Prepare_ExpiredGrant_ThrowsGrantNotActive()
    {
        time.Now = grant.ExpiresAt;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.PrepareAsync(grant.Id, item.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.GrantNotActive, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
        Assert.Equal("EXPIRED", error.Extensions["reason"]);
        Assert.Single(repository.Logs);
        Assert.Equal(HandoverAccessOutcome.Denied, repository.Logs[0].Outcome);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Prepare_OneSecondBeforeExpiry_Succeeds()
    {
        time.Now = grant.ExpiresAt.AddSeconds(-1);

        PreparedDownload download = await service.PrepareAsync(grant.Id, item.Id, CancellationToken.None);

        Assert.Equal(ContentLength, download.Length);
    }

    [Theory]
    [InlineData(HandoverGrantStatus.Blocked, "BLOCKED")]
    [InlineData(HandoverGrantStatus.Revoked, "REVOKED")]
    public async Task Prepare_BlockedOrRevokedGrant_Throws(HandoverGrantStatus status, string reason)
    {
        grant.Status = status;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.PrepareAsync(grant.Id, item.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.GrantNotActive, error.Code);
        Assert.Equal(reason, error.Extensions["reason"]);
    }

    [Fact]
    public async Task Prepare_PlanClosed_IsStillAllowed()
    {
        data.Plan.Status = PlanStatus.Closed;

        PreparedDownload download = await service.PrepareAsync(grant.Id, item.Id, CancellationToken.None);

        Assert.Equal(ContentLength, download.Length);
    }

    [Fact]
    public async Task Prepare_PlanDeathCaseOpen_Throws()
    {
        data.Plan.Status = PlanStatus.DeathCaseOpen;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.PrepareAsync(grant.Id, item.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.GrantNotActive, error.Code);
    }

    [Fact]
    public async Task Prepare_OtherUsersGrant_ThrowsNotFoundWithoutLogging()
    {
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.PrepareAsync(grant.Id, item.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
        Assert.Equal(AppErrorType.NotFound, error.Type);
        Assert.Empty(repository.Logs);
    }

    [Fact]
    public async Task Prepare_ItemNotInGrantSnapshot_ThrowsNotFound()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.PrepareAsync(grant.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotFound, error.Code);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Prepare_Success_LogsSucceeded()
    {
        await service.PrepareAsync(grant.Id, item.Id, CancellationToken.None);

        Assert.Single(repository.Logs);
        HandoverAccessLog log = repository.Logs[0];
        Assert.Equal(HandoverAccessOutcome.Succeeded, log.Outcome);
        Assert.Equal(grant.Id, log.GrantId);
        Assert.Equal(item.Id, log.SnapshotItemId);
        Assert.Equal(claimantId, log.UserId);
        Assert.Equal(Now, log.At);
        Assert.True(unitOfWork.SaveCount >= 1);
    }
}
