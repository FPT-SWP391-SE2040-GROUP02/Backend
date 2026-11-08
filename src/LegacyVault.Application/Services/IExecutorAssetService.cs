using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Người thực thi xem danh sách tài sản của kho, theo từng gói bàn giao. Chỉ danh sách, không có nội dung.</summary>
public interface IExecutorAssetService
{
    /// <summary>
    /// Các gói Ready của kho vaultId kèm người nhận và tài sản (tên, loại, dung lượng).
    /// Chỉ Người thực thi đang hiệu lực (đã chấp nhận, không phải chủ kho) được gọi; người khác nhận 404 như kho không tồn tại.
    /// Lỗi: 404 VAULT_NOT_FOUND.
    /// </summary>
    Task<List<ExecutorVaultPackageResponse>> ListPackagesAsync(Guid vaultId, CancellationToken ct);
}
