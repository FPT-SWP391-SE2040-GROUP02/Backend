namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// JSON trả cho FE từ POST /api/v1/auth/google (api-contract §6.3).
/// </summary>
/// <remarks>
/// Hai dạng:
/// - SignedIn = true: có Session (access token), refresh token đã nằm trong cookie.
/// - SignedIn = false: có LinkTicket + MaskedEmail, FE hỏi mật khẩu rồi gọi /google/link-confirm.
/// Tách riêng khỏi GoogleLoginResult để refresh token (nằm trong AuthResult) không bao giờ lọt ra JSON.
/// </remarks>
public class GoogleLoginResponse
{
    public bool SignedIn { get; set; }

    public bool AccountCreated { get; set; }

    public string? ReturnUrl { get; set; }

    public AuthSessionResponse? Session { get; set; }

    public string? LinkTicket { get; set; }

    public string? MaskedEmail { get; set; }

    /// <summary>Chuyển kết quả nội bộ của service thành JSON an toàn để trả cho FE.</summary>
    public static GoogleLoginResponse From(GoogleLoginResult result)
    {
        return new GoogleLoginResponse
        {
            SignedIn = result.SignedIn,
            AccountCreated = result.AccountCreated,
            ReturnUrl = result.ReturnUrl,
            Session = result.Auth?.Session,
            LinkTicket = result.LinkTicket,
            MaskedEmail = result.MaskedEmail,
        };
    }
}
