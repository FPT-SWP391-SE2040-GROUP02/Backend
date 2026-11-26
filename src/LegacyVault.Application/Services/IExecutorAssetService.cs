using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Người thực thi xem danh sách tài sản của kho, theo từng người nhận. Chỉ danh sách, không có nội dung.</summary>
public interface IExecutorAssetService
{
    /// <summary>
    /// Các người nhận của kho vaultId kèm tài sản được gán (tên, loại, dung lượng).
    /// Chỉ Người thực thi đang hiệu lực (đã chấp nhận, không phải chủ tài sản) được gọi; người khác nhận 404 như kho không tồn tại.
    /// Lỗi: 404 VAULT_NOT_FOUND.
    /// </summary>
    Task<List<ExecutorVaultRecipientResponse>> ListRecipientsAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Tên và loại các tài sản còn nội dung của kho, theo ngày tạo, kể cả tài sản chưa gán cho ai.
    /// Chỉ Người thực thi đang hiệu lực được gọi, không phụ thuộc đã bàn giao hay chưa. Lỗi: 404 VAULT_NOT_FOUND.
    /// </summary>
    Task<List<ExecutorAssetNameResponse>> ListAssetNamesAsync(Guid vaultId, CancellationToken ct);
}
