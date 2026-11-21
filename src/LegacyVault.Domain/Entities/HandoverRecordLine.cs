namespace LegacyVault.Domain.Entities;

/// <summary>Một dòng biên bản: người nhận nào nhận tài sản nào (bảng HandoverRecordLines).</summary>
/// <remarks>
/// Mỗi cặp người nhận - tài sản chỉ có một dòng (unique RecordId + BeneficiaryId + AssetId).
/// Một tài sản có thể giao cho nhiều người nhận cùng lúc (đồng thừa kế).
/// </remarks>
public class HandoverRecordLine
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RecordId { get; set; }
    public HandoverRecord Record { get; set; } = null!;
    public Guid BeneficiaryId { get; set; }
    public Guid AssetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
