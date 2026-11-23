namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một dòng gán: tài sản này được gán cho người nhận này. Chỉ có id; tên lấy từ danh sách tài sản và người nhận của kho.</summary>
public class AssetAssignmentResponse
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }

    public Guid BeneficiaryId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
