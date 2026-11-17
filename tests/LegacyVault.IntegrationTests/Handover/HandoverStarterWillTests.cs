using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>Repository giả của WillVersions, chỉ giữ dữ liệu trong List.</summary>
public sealed class FakeWillVersionRepository : IWillVersionRepository
{
    public List<WillVersion> Versions { get; } = new List<WillVersion>();

    public Task<WillVersion?> FindLatestByVaultIdAsync(Guid vaultId, CancellationToken ct)
    {
        WillVersion? latest = null;
        foreach (WillVersion version in Versions)
        {
            if (version.VaultId != vaultId)
            {
                continue;
            }
            if (latest == null || version.VersionNo > latest.VersionNo)
            {
                latest = version;
            }
        }
        return Task.FromResult(latest);
    }

    public void Add(WillVersion version)
    {
        Versions.Add(version);
    }
}

/// <summary>Bàn giao chỉ theo bản di chúc đã ký gần nhất: phần thêm/đổi chưa ký lại bị bỏ qua, phần đã xóa không bàn giao.</summary>
public sealed class HandoverStarterWillTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakeHandoverRepository repository = new FakeHandoverRepository();
    private readonly FakeBeneficiaryDataProtector protector = new FakeBeneficiaryDataProtector();
    private readonly FakeTokenService tokens = new FakeTokenService();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FakeWillVersionRepository willVersions = new FakeWillVersionRepository();
    private readonly HandoverStarter starter;

    public HandoverStarterWillTests()
    {
        FakeInvitationSender sender = new FakeInvitationSender(unitOfWork);
        starter = new HandoverStarter(vaults, repository, protector, tokens, sender, unitOfWork,
            new FixedTimeProvider(Now), null, null, willVersions);
        vaults.Vaults.Add(data.Vault);
    }

    private void Publish()
    {
        repository.Designations.Clear();
        repository.Designations.AddRange(data.Designations);
        foreach (KeyValuePair<Guid, LegacyVault.Application.Security.BeneficiaryPersonalData> pair in data.People)
        {
            protector.Data[pair.Key] = pair.Value;
        }
    }

    /// <summary>Ký di chúc: chụp cấu hình hiện tại thành phiên bản kế tiếp.</summary>
    private void Sign()
    {
        int versionNo = willVersions.Versions.Count + 1;
        WillSnapshot snapshot = WillVersionRules.BuildSnapshot(data.Vault.Containers);
        willVersions.Add(WillVersionRules.CreateVersion(data.Vault.Id, versionNo, versionNo, snapshot, Now.AddDays(-10)));
    }

    private async Task<HandoverStartResult> StartAsync()
    {
        Publish();
        return await starter.StartAsync(data.Vault.Id, CancellationToken.None);
    }

    [Fact]
    public async Task Start_WithoutAnyWillVersion_FollowsCurrentConfiguration()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", "a@example.com");
        data.Designate(container, "Người B", "b@example.com");

        HandoverStartResult result = await StartAsync();

        Assert.Equal(2, result.TurnCount);
        Assert.Equal(2, repository.Turns[0].Items.Count);
    }

    [Fact]
    public async Task Start_AfterSigningWithoutChanges_HandsOverEverything()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", "a@example.com");
        Sign();

        HandoverStartResult result = await StartAsync();

        Assert.Equal(1, result.TurnCount);
        Assert.Equal(2, repository.Turns[0].Items.Count);
    }

    [Fact]
    public async Task Start_RemovedContainerAfterSigning_IsNotHandedOverButTheRestIs()
    {
        Container kept = data.AddContainer("Gói giữ", ContainerStatus.Ready, 1);
        Container removed = data.AddContainer("Gói xóa", ContainerStatus.Ready, 1);
        data.Designate(kept, "Người A", "a@example.com");
        data.Designate(removed, "Người B", "b@example.com");
        Sign();
        // Chủ kho xóa gói sau khi ký: gói không còn READY.
        removed.Status = ContainerStatus.Removed;

        HandoverStartResult result = await StartAsync();

        Assert.Equal(1, result.TurnCount);
        Assert.Equal("Gói giữ", repository.Turns[0].ContainerName);
    }

    [Fact]
    public async Task Start_ContainerAddedAfterSigning_IsSkipped()
    {
        Container signed = data.AddContainer("Gói đã ký", ContainerStatus.Ready, 1);
        data.Designate(signed, "Người A", "a@example.com");
        Sign();
        Container added = data.AddContainer("Gói mới", ContainerStatus.Ready, 1);
        data.Designate(added, "Người B", "b@example.com");

        HandoverStartResult result = await StartAsync();

        Assert.Equal(1, result.TurnCount);
        Assert.Equal("Gói đã ký", repository.Turns[0].ContainerName);
    }

    [Fact]
    public async Task Start_RecipientAddedAfterSigning_IsSkipped()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        Designation first = data.Designate(container, "Người đã ký", "a@example.com");
        Sign();
        data.Designate(container, "Người mới", "b@example.com");

        HandoverStartResult result = await StartAsync();

        Assert.Equal(1, result.TurnCount);
        Assert.Equal(first.BeneficiaryId, repository.Turns[0].BeneficiaryId);
    }

    [Fact]
    public async Task Start_RecipientReplacedAfterSigning_HandsOverToNobodyForThatContainer()
    {
        Container replaced = data.AddContainer("Gói đổi người", ContainerStatus.Ready, 1);
        Designation oldDesignation = data.Designate(replaced, "Người cũ", "old@example.com");
        Container other = data.AddContainer("Gói khác", ContainerStatus.Ready, 1);
        data.Designate(other, "Người C", "c@example.com");
        Sign();
        // Gỡ người cũ, gắn người mới (chưa ký lại).
        replaced.Designations.Remove(oldDesignation);
        data.Designations.Remove(oldDesignation);
        data.Designate(replaced, "Người mới", "new@example.com");

        HandoverStartResult result = await StartAsync();

        Assert.Equal(1, result.TurnCount);
        Assert.Equal("Gói khác", repository.Turns[0].ContainerName);
    }

    [Fact]
    public async Task Start_AssetAddedAfterSigning_IsNotPinnedInTheSnapshot()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", "a@example.com");
        Sign();
        Asset signedFirst = container.Assets[0].Asset;
        Asset signedSecond = container.Assets[1].Asset;
        // Thêm một tài sản mới vào gói sau khi ký.
        Asset added = new Asset
        {
            VaultId = data.Vault.Id, Vault = data.Vault, Kind = AssetKind.File, Name = "Tài sản thêm sau",
            FileName = "moi.bin", ContentType = "application/pdf", SizeBytes = 5, StorageKey = "k-moi",
            EncryptionKeyId = "k1", CreatedAt = Now.AddDays(-2),
        };
        container.Assets.Add(new ContainerAsset
        {
            ContainerId = container.Id, Container = container, AssetId = added.Id, Asset = added, AddedAt = Now.AddDays(-1),
        });

        await StartAsync();

        List<Guid> pinned = new List<Guid>();
        foreach (HandoverSnapshotItem item in repository.Turns[0].Items)
        {
            pinned.Add(item.AssetId);
        }
        Assert.Equal(2, pinned.Count);
        Assert.Contains(signedFirst.Id, pinned);
        Assert.Contains(signedSecond.Id, pinned);
        Assert.DoesNotContain(added.Id, pinned);
    }

    [Fact]
    public async Task Start_AssetRemovedAfterSigning_IsNotPinnedButOthersAre()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", "a@example.com");
        Sign();
        Asset removedAsset = container.Assets[0].Asset;
        container.Assets.RemoveAt(0);

        await StartAsync();

        Assert.Single(repository.Turns[0].Items);
        Assert.NotEqual(removedAsset.Id, repository.Turns[0].Items[0].AssetId);
    }

    [Fact]
    public async Task Start_AllSignedAssetsReplacedByUnsignedOnes_FindsNothingToHandOver()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", "a@example.com");
        Sign();
        container.Assets.RemoveAt(0);
        Asset added = new Asset
        {
            VaultId = data.Vault.Id, Vault = data.Vault, Kind = AssetKind.File, Name = "Tài sản thay",
            FileName = "thay.bin", ContentType = "application/pdf", SizeBytes = 5, StorageKey = "k-thay",
            EncryptionKeyId = "k1", CreatedAt = Now.AddDays(-2),
        };
        container.Assets.Add(new ContainerAsset
        {
            ContainerId = container.Id, Container = container, AssetId = added.Id, Asset = added, AddedAt = Now.AddDays(-1),
        });
        Publish();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => starter.StartAsync(data.Vault.Id, CancellationToken.None));

        Assert.Equal(ErrorCode.HandoverNotAllowed, error.Code);
        Assert.Equal("NOTHING_TO_HAND_OVER", error.Extensions["reason"]);
        Assert.Empty(repository.Turns);
        Assert.Equal(PlanStatus.DeathCaseOpen, data.Plan.Status);
    }

    [Fact]
    public async Task Start_AfterSigningAgain_HandsOverTheNewParts()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", "a@example.com");
        Sign();
        data.Designate(container, "Người mới", "new@example.com");
        Container added = data.AddContainer("Gói mới", ContainerStatus.Ready, 1);
        data.Designate(added, "Người C", "c@example.com");
        // Ký lại: bản chụp mới gồm cả phần vừa thêm.
        Sign();

        HandoverStartResult result = await StartAsync();

        Assert.Equal(3, result.TurnCount);
        Assert.Equal(2, willVersions.Versions.Count);
    }
}
