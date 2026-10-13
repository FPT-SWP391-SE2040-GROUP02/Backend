using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/me/login-methods/password/remove: bỏ mật khẩu app, chỉ còn Google (AUTH-03).
/// Chỉ bỏ được khi đã liên kết Google. Xác thực lại bằng CẢ mật khẩu hiện tại và Google ID token mới.
/// </summary>
public class RemovePasswordRequest
{
    [Required(ErrorMessage = "Cần nhập mật khẩu hiện tại để xác thực lại.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cần đăng nhập lại Google để xác thực.")]
    [MaxLength(4096)]
    public string GoogleIdToken { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Nonce { get; set; }
}
