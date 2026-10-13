using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Kho của người đang đăng nhập (SRS §5.2 bước 1, SETUP-12).
/// </summary>
/// <remarks>
/// Lỗi chung: 401 SESSION_EXPIRED; 404 VAULT_NOT_FOUND cả khi kho thuộc người khác (không lộ sự tồn tại, SEC-04).
/// </remarks>
public interface IVaultService
{
    /// <summary>
    /// "Tạo kho của tôi": đã có kho thì mở lại kho đó (KỂ CẢ CLOSED, AC-104); chưa có thì tạo đúng một kho
    /// OPEN + kế hoạch DRAFT (ConfigVersion = 1) + quyền dịch vụ FREE 20 MiB, 0 suất — trong một giao dịch.
    /// Gọi lặp hoặc song song vẫn ra cùng một kho (IVaultRepository.TryAddAsync false → đọc lại kho vừa tạo, AC-89).
    /// Đọc lại user từ DB, không tin thông tin trong token.
    /// Lỗi: 403 ACCOUNT_NOT_ACTIVE, 403 EMAIL_NOT_VERIFIED (AC-01).
    /// </summary>
    Task<OpenVaultResult> OpenOrCreateMyVaultAsync(CreateVaultRequest? request, CancellationToken ct);

    /// <summary>Kho của mình. Chưa tạo → 404 VAULT_NOT_FOUND.</summary>
    Task<VaultResponse> GetMyVaultAsync(CancellationToken ct);

    /// <summary>Một kho theo Id. Không có / của người khác → 404 VAULT_NOT_FOUND.</summary>
    Task<VaultResponse> GetVaultAsync(Guid vaultId, CancellationToken ct);

    /// <summary>
    /// Sửa tên/mô tả kho; KHÔNG tăng Plan.ConfigVersion (tên kho không thuộc cấu hình bàn giao).
    /// request.Etag = ETag client đang thấy; ghi bằng UPDATE có điều kiện để không ghi đè thay đổi của tab khác.
    /// Lỗi: 409 EDIT_CONFLICT (etag cũ/sai định dạng), 400 VALIDATION_ERROR,
    /// 409 VAULT_NOT_EDITABLE (+reason: VAULT_CLOSED / PLAN_LOCKED / SERVICE_READ_ONLY).
    /// </summary>
    Task<VaultResponse> UpdateVaultAsync(Guid vaultId, UpdateVaultRequest request, CancellationToken ct);
}
