using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Mã PIN 6 chữ số của kho: chặn người dùng tài khoản Owner (không phải Owner thật) khỏi các thao tác nhạy cảm.
/// Mọi hàm chỉ chạy cho Owner của kho; kho của người khác trả 404 VAULT_NOT_FOUND (SEC-04).
/// </summary>
public interface IVaultPinService
{
    Task<VaultPinStatusResponse> GetStatusAsync(Guid vaultId, CancellationToken ct);

    /// <summary>Đặt PIN lần đầu. Đã có PIN → 409 VAULT_PIN_ALREADY_SET.</summary>
    Task<VaultPinStatusResponse> SetPinAsync(Guid vaultId, SetVaultPinRequest request, CancellationToken ct);

    /// <summary>Đổi PIN bằng PIN cũ (tính vào bộ đếm sai và khóa như mọi lần nhập PIN).</summary>
    Task<VaultPinStatusResponse> ChangePinAsync(Guid vaultId, ChangeVaultPinRequest request, CancellationToken ct);

    /// <summary>Quên PIN: nhập đúng mật khẩu tài khoản thì đặt PIN mới ngay và gỡ khóa.</summary>
    Task<VaultPinStatusResponse> ResetPinAsync(Guid vaultId, ResetVaultPinRequest request, CancellationToken ct);

    /// <summary>
    /// Kiểm tra PIN gửi kèm (header X-Vault-Pin) trước một thao tác được bảo vệ.
    /// Kho chưa có PIN và chưa có tài sản thì cho qua; có tài sản mà chưa có PIN → 409 VAULT_PIN_NOT_SET.
    /// </summary>
    Task VerifyAsync(Guid vaultId, string? pin, CancellationToken ct);

    /// <summary>Chặn tạo thêm tài sản khi kho đã có tài sản mà chưa đặt PIN (409 VAULT_PIN_NOT_SET).</summary>
    Task EnsurePinSetBeforeNewAssetAsync(Guid vaultId, CancellationToken ct);
}
