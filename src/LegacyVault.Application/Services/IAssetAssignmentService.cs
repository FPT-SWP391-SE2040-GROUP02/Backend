using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Chủ kho gán trực tiếp từng tài sản cho từng người nhận. Tài sản gán cho nhiều người thì những người đó nhận chung.
/// </summary>
/// <remarks>
/// Luật chung khi cài đặt:
/// - Mọi thay đổi gán tăng HandoverPlan.ConfigVersion.
/// - Thao tác thêm người nhận chặn khi kho đóng, kế hoạch bị khóa hoặc dịch vụ đang ân hạn, và chặn hạng Free;
///   thao tác chỉ gỡ vẫn cho khi dịch vụ đang ân hạn.
/// - Suất người nhận tính theo số người khác nhau có ít nhất một dòng gán trong kho.
/// - Kế hoạch đang chạy mà thao tác làm kho không còn dòng gán nào thì Chủ kho phải xác nhận, kế hoạch về bản nháp.
/// Lỗi chung: 401 SESSION_EXPIRED, 404 VAULT_NOT_FOUND (không có kho hoặc kho người khác),
/// 409 VAULT_NOT_EDITABLE (+reason: VAULT_CLOSED / PLAN_LOCKED / SERVICE_READ_ONLY).
/// </remarks>
public interface IAssetAssignmentService
{
    /// <summary>Mọi dòng gán của kho, theo thứ tự gán.</summary>
    Task<List<AssetAssignmentResponse>> GetAssignmentsAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Đặt lại toàn bộ người nhận của một tài sản; trả các dòng gán của tài sản sau khi đổi.
    /// Lỗi: 404 ASSET_NOT_FOUND, 404 BENEFICIARY_NOT_FOUND, 403 PAID_TIER_REQUIRED (hạng Free khi thêm người),
    /// 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED, 409 EDIT_CONFLICT.
    /// </summary>
    Task<List<AssetAssignmentResponse>> SetAssetRecipientsAsync(
        Guid vaultId, Guid assetId, SetAssetRecipientsRequest request, CancellationToken ct);

    /// <summary>
    /// Gỡ một người nhận khỏi một tài sản; trả các dòng gán còn lại của tài sản. Cặp chưa được gán thì trả nguyên trạng.
    /// Lỗi: 404 ASSET_NOT_FOUND, 409 PLAN_DEACTIVATION_CONFIRMATION_REQUIRED, 409 EDIT_CONFLICT.
    /// </summary>
    Task<List<AssetAssignmentResponse>> RemoveAssignmentAsync(
        Guid vaultId, Guid assetId, Guid beneficiaryId, ConfirmPlanImpactRequest? request, CancellationToken ct);
}
