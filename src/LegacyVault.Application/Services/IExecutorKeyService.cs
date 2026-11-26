using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Mã nhận bàn giao do server tạo (lúc kích hoạt kế hoạch). Hàm của Owner chỉ chạy cho Owner của kho; kho người khác trả 404.
/// Đổi mã chỉ được khi kế hoạch chưa khóa (409 VAULT_NOT_EDITABLE nếu đã có báo tin).
/// </summary>
public interface IExecutorKeyService
{
    Task<ExecutorKeyStatusResponse> GetStatusAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Chủ tài sản đổi mã: mã mới thay mã cũ ngay, không trả mã gốc (mã do server giữ). Kho chưa có mã → 409 EXECUTOR_KEY_NOT_ISSUED.
    /// </summary>
    Task<ExecutorKeyStatusResponse> ReissueAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Người thực thi đã chấp nhận đọc mã nhận, chỉ khi kế hoạch đã HandoverInProgress
    /// (chưa tới lúc → 409 HANDOVER_NOT_ALLOWED, reason HANDOVER_NOT_STARTED).
    /// </summary>
    Task<ExecutorHandoverKeyResponse> GetKeyForExecutorAsync(Guid vaultId, CancellationToken ct);
}
