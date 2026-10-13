using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/auth/google/link-confirm (AUTH-02): email Google trùng tài khoản app chưa liên kết,
/// người dùng nhập mật khẩu tài khoản app để chứng minh là chủ rồi mới liên kết.
/// </summary>
public class ConfirmGoogleLinkRequest
{
    /// <summary>Vé nhận từ GoogleLoginResult.LinkTicket.</summary>
    [Required(ErrorMessage = "Thiếu vé liên kết.")]
    [MaxLength(256)]
    public string LinkTicket { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}
