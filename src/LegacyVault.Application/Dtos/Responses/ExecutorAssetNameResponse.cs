namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Một tài sản của kho nhìn từ Người thực thi trước khi bàn giao (GET /api/v1/executor/vaults/{vaultId}/asset-names):
/// chỉ tên và loại. Cố ý không có mã, dung lượng, người nhận hay nội dung.
/// </summary>
public class ExecutorAssetNameResponse
{
    /// <summary>Tên do Chủ kho đặt.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>FILE hoặc TEXT.</summary>
    public string Kind { get; set; } = string.Empty;
}
