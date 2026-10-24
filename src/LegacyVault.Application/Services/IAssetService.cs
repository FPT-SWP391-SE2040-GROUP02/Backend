using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Tài sản số trong kho của người đang đăng nhập (§5.2; QUOTA-01, QUOTA-02, ASSET-02).
/// </summary>
/// <remarks>
/// Luật chung:
/// - Tài sản thuộc kho; đưa vào gói là việc của IContainerService.AddAssetAsync (dùng IAssetRepository.FindByIdAsync).
/// - Tạo tài sản KHÔNG tăng Plan.ConfigVersion (tài sản chưa ở gói nào thì chưa thuộc cấu hình bàn giao).
/// - Hạng Free được lưu tài sản trong quota 20 MiB; chỉ bị chặn khi đưa vào gói (SETUP-17).
/// - Tạo mới là thao tác "thêm": chặn khi kho CLOSED / kế hoạch bị khóa / dịch vụ chỉ đọc (OPLAN-01).
/// - Không trả nội dung tài sản trong metadata/danh sách.
/// Lỗi chung: 401 SESSION_EXPIRED, 404 VAULT_NOT_FOUND (không có kho / kho người khác),
/// 409 VAULT_NOT_EDITABLE (+reason).
/// </remarks>
public interface IAssetService
{
    /// <summary>
    /// Tạo tài sản dạng tệp, phiên bản 1.
    /// Lỗi: 400 VALIDATION_ERROR (thiếu tệp/tệp rỗng/tên quá dài),
    /// 409 STORAGE_QUOTA_EXCEEDED (+storageQuotaBytes, storageRemainingBytes), 413 FILE_TOO_LARGE.
    /// </summary>
    Task<AssetResponse> CreateFileAssetAsync(Guid vaultId, CreateFileAssetRequest request, CancellationToken ct);

    /// <summary>Tạo tài sản dạng chữ, phiên bản 1. Lỗi như CreateFileAssetAsync.</summary>
    Task<AssetResponse> CreateTextAssetAsync(Guid vaultId, CreateTextAssetRequest request, CancellationToken ct);

    /// <summary>
    /// Một trang tài sản của kho. SearchTerm lọc theo tên;
    /// SortBy cho phép "name", "createdAt" (mặc định createdAt tăng dần), giá trị khác → 400.
    /// </summary>
    Task<PaginatedList<AssetResponse>> GetAssetsAsync(Guid vaultId, PaginationRequest pagination, CancellationToken ct);

    /// <summary>Metadata một tài sản. Lỗi: 404 ASSET_NOT_FOUND (cả khi tài sản thuộc kho khác).</summary>
    Task<AssetResponse> GetAssetAsync(Guid vaultId, Guid assetId, CancellationToken ct);

    /// <summary>
    /// Owner tải nội dung tài sản của mình. Kiểm toàn vẹn trước khi gửi
    /// byte đầu tiên. Lỗi: 404 ASSET_NOT_FOUND (kể cả nội dung đã dọn),
    /// ContentIntegrityException khi nội dung hỏng.
    /// </summary>
    Task<PreparedDownload> PrepareContentAsync(Guid vaultId, Guid assetId, CancellationToken ct);

    /// <summary>
    /// Đổi tên/mô tả (không đổi nội dung). Lỗi: 409 EDIT_CONFLICT (etag lệch),
    /// 409 VAULT_NOT_EDITABLE (kể cả reason HOLD_ACTIVE).
    /// </summary>
    Task<AssetResponse> UpdateAssetAsync(Guid vaultId, Guid assetId, UpdateAssetRequest request, CancellationToken ct);

    /// <summary>
    /// Xóa tài sản cùng nội dung. Lỗi: 409 ASSET_IN_USE (reason IN_CONTAINER | PINNED_IN_HANDOVER),
    /// 409 VAULT_NOT_EDITABLE (kể cả reason HOLD_ACTIVE).
    /// </summary>
    Task DeleteAssetAsync(Guid vaultId, Guid assetId, CancellationToken ct);
}
