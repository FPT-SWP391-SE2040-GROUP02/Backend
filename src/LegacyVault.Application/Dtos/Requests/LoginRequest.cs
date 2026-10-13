using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/auth/login.</summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>"Ghi nhớ đăng nhập": cookie sống qua khi tắt trình duyệt, nhưng phiên vẫn tối đa 12 giờ (NFR-06).</summary>
    public bool RememberMe { get; set; }
}
