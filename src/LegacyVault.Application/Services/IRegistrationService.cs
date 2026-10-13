using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đăng ký tài khoản app và xác minh email (ROLE-01, AUTH-04).
/// </summary>
/// <remarks>
/// - Tài khoản mới: Role = User, Status = Active, EmailVerifiedAt = null. Chưa xác minh vẫn đăng nhập được
///   nhưng không tạo được kho (AC-01).
/// - Link xác minh dùng một lần, có hạn (gợi ý 24 giờ); chỉ lưu hash token.
/// - Không tạo kho, không gán quan hệ Owner khi đăng ký (SETUP-15).
/// </remarks>
public interface IRegistrationService
{
    /// <summary>
    /// Đăng ký rồi gửi link xác minh. Luôn "thành công" kể cả khi email đã có tài khoản,
    /// để người lạ không dò được email nào đã đăng ký (khi đó có thể gửi email báo "bạn đã có tài khoản").
    /// Lỗi: 400 VALIDATION_ERROR.
    /// </summary>
    Task RegisterAsync(RegisterRequest request, CancellationToken ct);

    /// <summary>Xác minh email bằng token trong link. Lỗi: 400 INVALID_VERIFICATION_TOKEN (sai/hết hạn/đã dùng).</summary>
    Task<UserResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct);

    /// <summary>Gửi lại link xác minh (vô hiệu link cũ). Luôn "thành công", lý do giống RegisterAsync.</summary>
    Task ResendVerificationAsync(ResendVerificationRequest request, CancellationToken ct);
}
