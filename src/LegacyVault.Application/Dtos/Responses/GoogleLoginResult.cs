namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Kết quả nội bộ của đăng nhập Google, giữa service và controller.
/// </summary>
/// <remarks>
/// - SignedIn = true: Auth có giá trị, controller ghi cookie như đăng nhập thường.
/// - SignedIn = false (LINK_REQUIRED, AUTH-02): email Google trùng tài khoản app chưa liên kết.
///   Controller trả LinkTicket + MaskedEmail; FE hỏi mật khẩu rồi gọi link-confirm. Không ghi cookie.
/// </remarks>
public class GoogleLoginResult
{
    public bool SignedIn { get; set; }

    /// <summary>Có giá trị khi SignedIn = true.</summary>
    public AuthResult? Auth { get; set; }

    /// <summary>Vé gốc (chỉ trả một lần) khi cần xác nhận liên kết.</summary>
    public string? LinkTicket { get; set; }

    /// <summary>Email tài khoản app cần nhập mật khẩu, đã che bớt, ví dụ an***@gmail.com.</summary>
    public string? MaskedEmail { get; set; }

    /// <summary>ReturnUrl đã kiểm tra là đường dẫn nội bộ (AUTH-06); null nếu không có hoặc không hợp lệ.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>true khi lần đăng nhập này vừa tạo tài khoản mới từ Google.</summary>
    public bool AccountCreated { get; set; }

    /// <summary>Khác null khi Admin đã bật xác thực hai bước: chưa có phiên, FE hỏi mã TOTP.</summary>
    public TwoFactorChallengeResponse? TwoFactor { get; set; }
}
