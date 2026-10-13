namespace LegacyVault.Application.Security;

/// <summary>
/// Kiểm tra Google ID token do FE gửi lên (AUTH-01). Cài đặt ở Infrastructure.
/// </summary>
/// <remarks>
/// Cài đặt PHẢI kiểm tra: chữ ký theo khóa công khai của Google, <c>aud</c> = Client ID của app,
/// <c>iss</c> là accounts.google.com, <c>exp</c> còn hạn, <c>nonce</c> khớp nếu có.
/// https://developers.google.com/identity/gsi/web/guides/verify-google-id-token
/// Service chỉ tin những gì interface này trả về, không tự giải mã token.
/// </remarks>
public interface IGoogleTokenVerifier
{
    /// <summary>Token hợp lệ → thông tin người dùng. Sai chữ ký/aud/iss/hết hạn/sai nonce → null.</summary>
    Task<GoogleIdentity?> VerifyAsync(string idToken, string? expectedNonce, CancellationToken ct);
}
