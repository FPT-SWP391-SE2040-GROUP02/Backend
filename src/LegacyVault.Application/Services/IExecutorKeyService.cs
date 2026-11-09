using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Mảnh khóa Chủ kho giao cho Người thực thi. Mọi hàm chỉ chạy cho Owner của kho; kho người khác trả 404 VAULT_NOT_FOUND.
/// Cấp mới và cấp lại chỉ được khi kế hoạch chưa khóa (409 VAULT_NOT_EDITABLE nếu đã có báo tin).
/// </summary>
public interface IExecutorKeyService
{
    Task<ExecutorKeyStatusResponse> GetStatusAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Cấp mảnh lần đầu, trả mảnh gốc một lần duy nhất. Đã cấp → 409 EXECUTOR_KEY_ALREADY_ISSUED.</summary>
    Task<ExecutorKeyIssuedResponse> IssueAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Quên khóa: cấp mảnh mới, mảnh cũ hết hiệu lực ngay. Chưa cấp lần nào → 409 EXECUTOR_KEY_NOT_ISSUED.
    /// </summary>
    Task<ExecutorKeyIssuedResponse> ReissueAsync(Guid vaultId, CancellationToken ct);
}
