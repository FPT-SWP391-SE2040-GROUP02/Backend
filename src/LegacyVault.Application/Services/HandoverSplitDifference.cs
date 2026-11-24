namespace LegacyVault.Application.Services;

/// <summary>
/// Một chỗ biên bản khác cách chia ban đầu của Chủ kho (AssetAssignments), do HandoverRecordRules.FindSplitDifferences
/// tìm ra. Chỉ có id; service đổi sang tên khi trả về, không kèm CCCD.
/// </summary>
public sealed class HandoverSplitDifference
{
    /// <summary>Người nhận có trong biên bản nhưng không có trong cách chia của Chủ kho (thường do luật sư thêm).</summary>
    public const string RecipientAdded = "RECIPIENT_ADDED";

    /// <summary>Người nhận Chủ kho đã chia tài sản nhưng không có trong biên bản.</summary>
    public const string RecipientRemoved = "RECIPIENT_REMOVED";

    /// <summary>Tài sản đổi người nhận hoặc thêm, bớt người cùng nhận so với Chủ kho.</summary>
    public const string AssetRecipientsChanged = "ASSET_RECIPIENTS_CHANGED";

    /// <summary>RECIPIENT_ADDED, RECIPIENT_REMOVED hoặc ASSET_RECIPIENTS_CHANGED.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Người nhận được thêm hoặc bị bỏ; null với khác biệt theo tài sản.</summary>
    public Guid? BeneficiaryId { get; set; }

    /// <summary>Tài sản đổi người nhận; null với khác biệt theo người nhận.</summary>
    public Guid? AssetId { get; set; }

    /// <summary>Người nhận tài sản này theo Chủ kho (chỉ với ASSET_RECIPIENTS_CHANGED).</summary>
    public List<Guid> OwnerBeneficiaryIds { get; set; } = new List<Guid>();

    /// <summary>Người nhận tài sản này theo biên bản (chỉ với ASSET_RECIPIENTS_CHANGED).</summary>
    public List<Guid> RecordBeneficiaryIds { get; set; } = new List<Guid>();
}
