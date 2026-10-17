using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/auth/reset-password: token trong link email và mật khẩu mới.</summary>
/// <remarks>Quy tắc độ dài mật khẩu giống RegisterRequest.</remarks>
public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Thiếu token đặt lại mật khẩu.")]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự.")]
    [MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;

    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu nhập lại không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
