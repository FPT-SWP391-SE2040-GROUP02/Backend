namespace LegacyVault.Domain.Entities;

/// <summary>
/// Chủ kho gán trực tiếp một tài sản cho một người nhận (bảng AssetAssignments).
/// </summary>
/// <remarks>
/// - Một tài sản gán cho nhiều người thì mỗi người một dòng: các người đó cùng nhận chung tài sản và cùng phải ký.
/// - Mỗi cặp (tài sản, người nhận) chỉ có một dòng, chặn ở cơ sở dữ liệu bằng chỉ mục duy nhất.
/// - Suất người nhận tính theo số người khác nhau có ít nhất một dòng gán trong kho.
/// - Bản chụp di chúc chép các cặp này lúc Chủ kho ký; đổi gán về sau không làm đổi bản đã ký.
/// </remarks>
public class AssetAssignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid AssetId { get; set; }

    public Asset Asset { get; set; } = null!;

    public Guid BeneficiaryId { get; set; }

    public Beneficiary Beneficiary { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
