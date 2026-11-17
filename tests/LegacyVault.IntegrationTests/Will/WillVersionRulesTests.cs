using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Will;

/// <summary>Luật thuần của di chúc số: bản chụp chỉ có id, so sánh đếm đúng thêm/xóa/đổi người nhận.</summary>
public sealed class WillVersionRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);

    private WillSnapshot Current()
    {
        return WillVersionRules.BuildSnapshot(data.Vault.Containers);
    }

    [Fact]
    public void BuildSnapshot_OnlyReadyContainers_AndOnlyIds()
    {
        Container ready = data.AddContainer("Gói bí mật A", ContainerStatus.Ready, 2);
        data.Designate(ready, "Người nhận bí mật", "a@example.com");
        Container draft = data.AddContainer("Gói nháp", ContainerStatus.Draft, 1);
        data.Designate(draft, "Người B", "b@example.com");

        WillSnapshot snapshot = Current();
        string json = WillVersionRules.ToJson(snapshot);

        Assert.Single(snapshot.Recipients);
        Assert.Equal(2, snapshot.Assets.Count);
        Assert.Equal(ready.Id, snapshot.Recipients[0].ContainerId);
        // Không có tên gói, tên tài sản hay tên người nhận (SEC-07).
        Assert.DoesNotContain("bí mật", json);
        Assert.DoesNotContain("Gói", json);
        Assert.DoesNotContain("tai-san", json);
        Assert.Contains(ready.Id.ToString(), json);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", null);
        WillSnapshot original = Current();

        WillSnapshot restored = WillVersionRules.FromJson(WillVersionRules.ToJson(original));

        Assert.Equal(original.Recipients.Count, restored.Recipients.Count);
        Assert.Equal(original.Assets.Count, restored.Assets.Count);
        Assert.Equal(original.Recipients[0].BeneficiaryId, restored.Recipients[0].BeneficiaryId);
        Assert.Equal(original.Assets[1].AssetId, restored.Assets[1].AssetId);
    }

    [Fact]
    public void FromJson_BrokenJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => WillVersionRules.FromJson("{khong-phai-json"));
    }

    [Fact]
    public void Compare_NoChange_AllZero()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(container, "Người A", null);
        WillSnapshot signed = Current();

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.False(WillVersionRules.HasChanges(changes));
        Assert.Equal(WillVersionRules.NoChanges(), changes);
    }

    [Fact]
    public void Compare_RemovedAndAddedContainer()
    {
        Container kept = data.AddContainer("Gói giữ", ContainerStatus.Ready, 1);
        data.Designate(kept, "Người A", null);
        Container removed = data.AddContainer("Gói xóa", ContainerStatus.Ready, 3);
        data.Designate(removed, "Người B", null);
        WillSnapshot signed = Current();
        removed.Status = ContainerStatus.Removed;
        Container added = data.AddContainer("Gói mới", ContainerStatus.Ready, 2);
        data.Designate(added, "Người C", null);

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.Equal(1, changes.RemovedContainers);
        Assert.Equal(1, changes.AddedContainers);
        // Tài sản/người nhận của gói thêm hoặc xóa không tính riêng.
        Assert.Equal(0, changes.RemovedAssets);
        Assert.Equal(0, changes.AddedAssets);
        Assert.Equal(0, changes.RemovedRecipients);
        Assert.Equal(0, changes.AddedRecipients);
        Assert.Equal(0, changes.ChangedRecipientCount);
        Assert.True(WillVersionRules.HasChanges(changes));
    }

    [Fact]
    public void Compare_AssetsAndRecipientsInsideAnExistingContainer()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 3);
        Designation oldRecipient = data.Designate(container, "Người cũ", null);
        WillSnapshot signed = Current();

        // Gỡ một tài sản, thêm hai tài sản; thay người nhận bằng hai người mới.
        container.Assets.RemoveAt(0);
        for (int i = 0; i < 2; i++)
        {
            Asset asset = new Asset { VaultId = data.Vault.Id, Vault = data.Vault, Name = "mới " + i, StorageKey = "k" + i };
            container.Assets.Add(new ContainerAsset
            {
                ContainerId = container.Id, Container = container, AssetId = asset.Id, Asset = asset, AddedAt = Now,
            });
        }
        container.Designations.Remove(oldRecipient);
        data.Designate(container, "Người mới 1", null);
        data.Designate(container, "Người mới 2", null);

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.Equal(0, changes.RemovedContainers);
        Assert.Equal(0, changes.AddedContainers);
        Assert.Equal(1, changes.RemovedAssets);
        Assert.Equal(2, changes.AddedAssets);
        Assert.Equal(1, changes.RemovedRecipients);
        Assert.Equal(2, changes.AddedRecipients);
        Assert.Equal(1, changes.ChangedRecipientCount);
    }

    [Fact]
    public void Compare_ContainerThatBecomesDraft_CountsAsRemoved()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        data.Designate(container, "Người A", null);
        WillSnapshot signed = Current();
        container.Status = ContainerStatus.Draft;

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.Equal(1, changes.RemovedContainers);
    }

    [Fact]
    public void FilterDesignations_KeepsOnlySignedPairs()
    {
        Container container = data.AddContainer("Gói A", ContainerStatus.Ready, 1);
        Designation signedPair = data.Designate(container, "Người A", null);
        WillSnapshot signed = Current();
        Designation unsignedPair = data.Designate(container, "Người B", null);

        List<Designation> effective = WillVersionRules.FilterDesignations(signed, data.Designations);

        Assert.Single(effective);
        Assert.Equal(signedPair.Id, effective[0].Id);
        Assert.DoesNotContain(unsignedPair, effective);
    }

    [Fact]
    public void SignedAssetIds_ReturnsOnlyAssetsOfThatContainer()
    {
        Container first = data.AddContainer("Gói A", ContainerStatus.Ready, 2);
        data.Designate(first, "Người A", null);
        Container second = data.AddContainer("Gói B", ContainerStatus.Ready, 1);
        data.Designate(second, "Người B", null);
        WillSnapshot signed = Current();

        HashSet<Guid> ids = WillVersionRules.SignedAssetIds(signed, first.Id);

        Assert.Equal(2, ids.Count);
        Assert.DoesNotContain(second.Assets[0].AssetId, ids);
    }
}
