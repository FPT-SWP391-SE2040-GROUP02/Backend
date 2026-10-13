using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/me/login-methods/password: tài khoản chỉ có Google đặt thêm mật khẩu app (AUTH-03).
/// Xác thực lại bằng một Google ID token MỚI của đúng tài khoản Google đã liên kết.
/// </summary>
public class SetPasswordRequest
{
    [Required(ErrorMessage = "Cần đăng nhập lại Google để xác thực.")]
    [MaxLength(4096)]
    public string GoogleIdToken { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Nonce { get; set; }

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    [MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
