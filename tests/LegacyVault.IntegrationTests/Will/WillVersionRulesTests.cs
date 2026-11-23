using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.IntegrationTests.Handover;
using Xunit;

namespace LegacyVault.IntegrationTests.Will;

/// <summary>Luật thuần của di chúc số: bản chụp chỉ có id, so sánh đếm đúng cặp thêm và cặp gỡ.</summary>
public sealed class WillVersionRulesTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly HandoverTestData data = new HandoverTestData(Now);

    private WillSnapshot Current()
    {
        return WillVersionRules.BuildSnapshot(data.Assignments);
    }

    [Fact]
    public void BuildSnapshot_OneRowPerAssignment_AndOnlyIds()
    {
        List<Asset> assets = data.AddAssets("tai-san-bi-mat", 2);
        Beneficiary recipient = data.AddRecipient("Người nhận bí mật", "a@example.com");
        data.AssignAll(assets, recipient);

        WillSnapshot snapshot = Current();
        string json = WillVersionRules.ToJson(snapshot);

        Assert.Equal(2, snapshot.Assignments.Count);
        Assert.Equal(recipient.Id, snapshot.Assignments[0].BeneficiaryId);
        // Không có tên tài sản hay tên người nhận (SEC-07).
        Assert.DoesNotContain("bí mật", json);
        Assert.DoesNotContain("tai-san", json);
        Assert.Contains(recipient.Id.ToString(), json);
    }

    [Fact]
    public void BuildSnapshot_SharedAsset_HasOneRowPerRecipient()
    {
        Asset shared = data.AddAsset("Nhà");
        data.Assign(shared, data.AddRecipient("Người A", null));
        data.Assign(shared, data.AddRecipient("Người B", null));

        WillSnapshot snapshot = Current();

        Assert.Equal(2, snapshot.Assignments.Count);
        Assert.Equal(shared.Id, snapshot.Assignments[0].AssetId);
        Assert.Equal(shared.Id, snapshot.Assignments[1].AssetId);
        Assert.NotEqual(snapshot.Assignments[0].BeneficiaryId, snapshot.Assignments[1].BeneficiaryId);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        data.AssignAll(data.AddAssets("Tài sản", 2), data.AddRecipient("Người A", null));
        WillSnapshot original = Current();

        WillSnapshot restored = WillVersionRules.FromJson(WillVersionRules.ToJson(original));

        Assert.Equal(original.Assignments.Count, restored.Assignments.Count);
        Assert.Equal(original.Assignments[0].BeneficiaryId, restored.Assignments[0].BeneficiaryId);
        Assert.Equal(original.Assignments[1].AssetId, restored.Assignments[1].AssetId);
    }

    [Fact]
    public void FromJson_BrokenJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => WillVersionRules.FromJson("{khong-phai-json"));
    }

    [Fact]
    public void Compare_NoChange_AllZero()
    {
        data.AssignAll(data.AddAssets("Tài sản", 2), data.AddRecipient("Người A", null));
        WillSnapshot signed = Current();

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.False(WillVersionRules.HasChanges(changes));
        Assert.Equal(WillVersionRules.NoChanges(), changes);
    }

    [Fact]
    public void Compare_AddedAndRemovedAssignments_CountPairsAndAffectedRecipients()
    {
        // Bản ký: A nhận 3 tài sản
        List<Asset> assets = data.AddAssets("Tài sản", 3);
        Beneficiary a = data.AddRecipient("Người A", null);
        data.AssignAll(assets, a);
        WillSnapshot signed = Current();

        // Sau đó: gỡ A khỏi tài sản đầu, thêm B vào tài sản thứ hai và thứ ba (nhận chung với A)
        data.Assignments.RemoveAt(0);
        Beneficiary b = data.AddRecipient("Người B", null);
        data.Assign(assets[1], b);
        data.Assign(assets[2], b);

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.Equal(2, changes.AddedAssignments);
        Assert.Equal(1, changes.RemovedAssignments);
        Assert.Equal(2, changes.AffectedRecipientCount);
        Assert.True(WillVersionRules.HasChanges(changes));
    }

    [Fact]
    public void Compare_SamePairsInDifferentOrder_IsNoChange()
    {
        Asset first = data.AddAsset("Tài sản 1");
        Asset second = data.AddAsset("Tài sản 2");
        Beneficiary a = data.AddRecipient("Người A", null);
        data.Assign(first, a);
        data.Assign(second, a);
        WillSnapshot signed = Current();

        data.Assignments.Reverse();

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.False(WillVersionRules.HasChanges(changes));
    }

    [Fact]
    public void Compare_AssetMovedToAnotherRecipient_CountsOneAddedAndOneRemoved()
    {
        Asset asset = data.AddAsset("Nhà");
        Beneficiary a = data.AddRecipient("Người A", null);
        Beneficiary b = data.AddRecipient("Người B", null);
        AssetAssignment toA = data.Assign(asset, a);
        WillSnapshot signed = Current();

        data.Assignments.Remove(toA);
        data.Assign(asset, b);

        UnsignedWillChangesResponse changes = WillVersionRules.Compare(signed, Current());

        Assert.Equal(1, changes.AddedAssignments);
        Assert.Equal(1, changes.RemovedAssignments);
        Assert.Equal(2, changes.AffectedRecipientCount);
    }
}
