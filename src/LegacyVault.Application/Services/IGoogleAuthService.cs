    using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đăng nhập bằng Google (AUTH-01, AUTH-02, AUTH-04, AUTH-05, AUTH-06).
/// </summary>
/// <remarks>
/// Mở phiên phải dùng CHUNG logic với IAuthService.LoginAsync (refresh token, giới hạn phiên NFR-06),
/// không chép lại một bản riêng. Tên/ảnh Google không xác nhận danh tính hay eKYC (AUTH-05).
/// </remarks>
public interface IGoogleAuthService
{
    /// <summary>
    /// Đăng nhập/đăng ký bằng Google ID token:
    /// - sub đã liên kết → mở phiên (SignedIn = true).
    /// - sub chưa liên kết, email chưa có tài khoản → tạo tài khoản (Role = User, PasswordHash = null,
    ///   EmailVerifiedAt = now chỉ khi email_verified = true) rồi mở phiên (AccountCreated = true).
    /// - sub chưa liên kết, email trùng tài khoản app → KHÔNG tự gộp, trả LinkTicket (SignedIn = false, AUTH-02).
    /// ReturnUrl chỉ nhận đường dẫn nội bộ (AUTH-06).
    /// Lỗi: 401 INVALID_GOOGLE_TOKEN, 403 ACCOUNT_NOT_ACTIVE.
    /// </summary>
    Task<GoogleLoginResult> LoginWithGoogleAsync(GoogleLoginRequest request, CancellationToken ct);

    /// <summary>
    /// Xác nhận liên kết Google vào tài khoản app bằng mật khẩu tài khoản đó, rồi mở phiên.
    /// Sai mật khẩu được đếm như đăng nhập thường (SEC-10, theo ipRange).
    /// Lỗi: 400 INVALID_LINK_TICKET (sai/hết hạn/đã dùng), 401 INVALID_CREDENTIALS, 423 ACCOUNT_LOCKED,
    /// 403 ACCOUNT_NOT_ACTIVE, 409 GOOGLE_ACCOUNT_ALREADY_LINKED.
    /// </summary>
    Task<AuthResult> ConfirmGoogleLinkAsync(ConfirmGoogleLinkRequest request, string ipRange, CancellationToken ct);
}
