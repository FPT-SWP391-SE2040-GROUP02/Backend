using LegacyVault.Application.Services;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>So biên bản với cách chia ban đầu của Chủ kho (HandoverRecordRules.FindSplitDifferences), không cần database.</summary>
public sealed class HandoverSplitDifferenceRulesTests
{
    private static readonly Guid An = Guid.NewGuid();
    private static readonly Guid Binh = Guid.NewGuid();
    private static readonly Guid Cuong = Guid.NewGuid();
    private static readonly Guid House = Guid.NewGuid();
    private static readonly Guid Car = Guid.NewGuid();

    private static List<(Guid BeneficiaryId, Guid AssetId)> Lines(params (Guid BeneficiaryId, Guid AssetId)[] lines)
    {
        return new List<(Guid BeneficiaryId, Guid AssetId)>(lines);
    }

    [Fact]
    public void SameSplit_InAnyOrder_HasNoDifference()
    {
        // Chuẩn bị
        List<(Guid BeneficiaryId, Guid AssetId)> owner = Lines((An, House), (Binh, Car));
        List<(Guid BeneficiaryId, Guid AssetId)> record = Lines((Binh, Car), (An, House));

        // Hành động
        List<HandoverSplitDifference> differences = HandoverRecordRules.FindSplitDifferences(record, owner);

        // Kiểm tra
        Assert.Empty(differences);
    }

    [Fact]
    public void LawyerAddsARecipient_ListsTheRecipientAndTheAssetChange()
    {
        // Chuẩn bị: Chủ kho cho An nhà; luật sư thêm Cường cùng nhận nhà
        List<(Guid BeneficiaryId, Guid AssetId)> owner = Lines((An, House));
        List<(Guid BeneficiaryId, Guid AssetId)> record = Lines((An, House), (Cuong, House));

        // Hành động
        List<HandoverSplitDifference> differences = HandoverRecordRules.FindSplitDifferences(record, owner);

        // Kiểm tra
        Assert.Equal(2, differences.Count);
        Assert.Equal(HandoverSplitDifference.RecipientAdded, differences[0].Kind);
        Assert.Equal(Cuong, differences[0].BeneficiaryId);
        Assert.Equal(HandoverSplitDifference.AssetRecipientsChanged, differences[1].Kind);
        Assert.Equal(House, differences[1].AssetId);
        Assert.Equal(new List<Guid> { An }, differences[1].OwnerBeneficiaryIds);
        Assert.Equal(new List<Guid> { An, Cuong }, differences[1].RecordBeneficiaryIds);
    }

    [Fact]
    public void RecipientDroppedAndAssetMoved_AreBothListed()
    {
        // Chuẩn bị: Chủ kho cho An nhà, Bình xe; biên bản giao cả nhà và xe cho An
        List<(Guid BeneficiaryId, Guid AssetId)> owner = Lines((An, House), (Binh, Car));
        List<(Guid BeneficiaryId, Guid AssetId)> record = Lines((An, House), (An, Car));

        // Hành động
        List<HandoverSplitDifference> differences = HandoverRecordRules.FindSplitDifferences(record, owner);

        // Kiểm tra
        Assert.Equal(2, differences.Count);
        Assert.Equal(HandoverSplitDifference.RecipientRemoved, differences[0].Kind);
        Assert.Equal(Binh, differences[0].BeneficiaryId);
        Assert.Equal(HandoverSplitDifference.AssetRecipientsChanged, differences[1].Kind);
        Assert.Equal(Car, differences[1].AssetId);
        Assert.Equal(new List<Guid> { Binh }, differences[1].OwnerBeneficiaryIds);
        Assert.Equal(new List<Guid> { An }, differences[1].RecordBeneficiaryIds);
    }

    [Fact]
    public void AssetLeftOutOfTheRecord_IsAnAssetChange()
    {
        // Chuẩn bị: Chủ kho cho An nhà và xe; biên bản chỉ ghi nhà
        List<(Guid BeneficiaryId, Guid AssetId)> owner = Lines((An, House), (An, Car));
        List<(Guid BeneficiaryId, Guid AssetId)> record = Lines((An, House));

        // Hành động
        List<HandoverSplitDifference> differences = HandoverRecordRules.FindSplitDifferences(record, owner);

        // Kiểm tra
        HandoverSplitDifference only = Assert.Single(differences);
        Assert.Equal(HandoverSplitDifference.AssetRecipientsChanged, only.Kind);
        Assert.Equal(Car, only.AssetId);
        Assert.Empty(only.RecordBeneficiaryIds);
    }
}
