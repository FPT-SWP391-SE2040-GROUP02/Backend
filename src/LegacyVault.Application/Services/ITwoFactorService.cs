using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Xác thực hai bước (TOTP) cho tài khoản Admin. Tự nguyện bật; khi đã bật, mọi đăng nhập Admin
/// (mật khẩu hoặc Google) phải qua thử thách trước khi được cấp phiên.
/// </summary>
public interface ITwoFactorService
{
    Task<TwoFactorStatusResponse> GetStatusAsync(CancellationToken ct);

    /// <summary>Tạo khóa mới (thay khóa chưa xác nhận nếu có). Đã bật rồi → 409 TWO_FACTOR_ALREADY_ENABLED.</summary>
    Task<TwoFactorSetupResponse> SetupAsync(CancellationToken ct);

    /// <summary>Xác nhận mã đầu tiên để bật; trả mã khôi phục (hiển thị một lần).</summary>
    Task<TwoFactorEnableResponse> EnableAsync(TwoFactorCodeRequest request, CancellationToken ct);

    /// <summary>Tắt bằng mã TOTP hoặc mã khôi phục; xóa khóa và mã khôi phục.</summary>
    Task DisableAsync(TwoFactorCodeRequest request, CancellationToken ct);

    /// <summary>
    /// Dùng ở đăng nhập: nếu user là Admin đã bật 2FA thì tạo thử thách và trả về (chưa cấp phiên), ngược lại null.
    /// Gọi trong cùng transaction với bước kiểm tra mật khẩu/Google.
    /// </summary>
    Task<TwoFactorChallengeResponse?> ChallengeIfRequiredAsync(User user, bool rememberMe, CancellationToken ct);

    /// <summary>Hoàn tất đăng nhập bằng mã TOTP/mã khôi phục; cấp phiên như đăng nhập thường.</summary>
    Task<AuthResult> VerifyLoginAsync(TwoFactorVerifyRequest request, CancellationToken ct);
}
