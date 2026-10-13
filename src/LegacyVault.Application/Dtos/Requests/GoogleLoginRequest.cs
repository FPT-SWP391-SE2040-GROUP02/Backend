using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/auth/google (AUTH-01).</summary>
/// <remarks>
/// FE lấy idToken bằng Google Identity Services rồi gửi nguyên chuỗi; backend tự kiểm tra (IGoogleTokenVerifier).
/// Không gửi email/tên riêng: mọi thông tin chỉ lấy từ token đã kiểm tra.
/// </remarks>
public class GoogleLoginRequest
{
    [Required(ErrorMessage = "Thiếu Google ID token.")]
    [MaxLength(4096)]
    public string IdToken { get; set; } = string.Empty;

    /// <summary>Nonce FE đã truyền cho Google lúc lấy token (chống replay). Có thì backend bắt buộc khớp.</summary>
    [MaxLength(256)]
    public string? Nonce { get; set; }

    public bool RememberMe { get; set; }

    /// <summary>Đường dẫn nội bộ để quay lại sau đăng nhập, ví dụ trang lời mời (AUTH-06). Không nhận URL ngoài.</summary>
    [MaxLength(2048)]
    public string? ReturnUrl { get; set; }
}
