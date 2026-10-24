using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/me/login-methods/password/verify (AUTH-03). Chỉ kiểm tra mật khẩu hiện tại,
/// không đổi dữ liệu nào; FE dùng để mở bước chọn Google khi liên kết.
/// </summary>
public class VerifyPasswordRequest
{
    [Required(ErrorMessage = "Cần nhập mật khẩu hiện tại để xác thực lại.")]
    [MaxLength(128)]
    public string CurrentPassword { get; set; } = string.Empty;
}
