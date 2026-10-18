using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/auth/forgot-password: email của tài khoản quên mật khẩu.</summary>
public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
