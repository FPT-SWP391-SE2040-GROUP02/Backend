using LegacyVault.Application.Dtos.Requests;

namespace LegacyVault.Application.Services;

/// <summary>
/// Quên mật khẩu: gửi link đặt lại qua email và đặt mật khẩu mới bằng link đó.
/// </summary>
/// <remarks>
/// - Link dùng một lần, hạn 30 phút; chỉ lưu mã băm token.
/// - Chỉ tài khoản đang hoạt động và đã có mật khẩu mới nhận link. Tài khoản chỉ dùng Google không đặt mật khẩu
///   qua đường này.
/// - Đặt lại thành công thì thu hồi mọi phiên đăng nhập của tài khoản trên mọi thiết bị.
/// </remarks>
public interface IPasswordResetService
{
    /// <summary>
    /// Gửi link đặt lại mật khẩu. Luôn "thành công" dù email không có tài khoản, để người lạ không dò được
    /// email nào đã đăng ký. Trong 60 giây kể từ link gần nhất thì không gửi thêm, tránh dội thư vào hộp mail.
    /// Lỗi: 400 VALIDATION_ERROR.
    /// </summary>
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);

    /// <summary>
    /// Đặt mật khẩu mới bằng token trong link. Thành công thì vô hiệu token, các link khác chưa dùng
    /// và mọi refresh token của tài khoản.
    /// Lỗi: 400 VALIDATION_ERROR, 400 INVALID_RESET_TOKEN (sai, hết hạn, đã dùng hoặc tài khoản không còn hoạt động).
    /// </summary>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}
