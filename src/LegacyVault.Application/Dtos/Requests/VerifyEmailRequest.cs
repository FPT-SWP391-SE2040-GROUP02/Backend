using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/auth/verify-email: token lấy từ link trong email (AUTH-04).</summary>
public class VerifyEmailRequest
{
    [Required(ErrorMessage = "Thiếu token xác minh.")]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;
}
