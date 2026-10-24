using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Admin chọn tài khoản cần làm lại eKYC từ đầu (công cụ thử nghiệm, chỉ có ở Development).</summary>
public class DevEkycResetRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email tài khoản.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
