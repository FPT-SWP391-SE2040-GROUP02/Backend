using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/me/login-methods/google/unlink (AUTH-03). Chỉ bỏ được khi còn mật khẩu app;
/// xác thực lại bằng mật khẩu đó.
/// </summary>
public class UnlinkGoogleRequest
{
    [Required(ErrorMessage = "Cần nhập mật khẩu hiện tại để xác thực lại.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;
}
