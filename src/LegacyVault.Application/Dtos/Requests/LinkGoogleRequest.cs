using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/me/login-methods/google: user ĐANG đăng nhập thêm Google vào tài khoản (AUTH-03).
/// Thêm phương thức cần xác thực lại bằng mật khẩu hiện tại.
/// </summary>
public class LinkGoogleRequest
{
    [Required(ErrorMessage = "Thiếu Google ID token.")]
    [MaxLength(4096)]
    public string IdToken { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Nonce { get; set; }

    [Required(ErrorMessage = "Cần nhập mật khẩu hiện tại để xác thực lại.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;
}
