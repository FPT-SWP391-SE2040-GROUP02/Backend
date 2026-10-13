namespace LegacyVault.Application.Exceptions;

/// <summary>
/// Google ID token sai chữ ký, sai Client ID, hết hạn hoặc sai nonce (401 INVALID_GOOGLE_TOKEN, AUTH-01).
/// </summary>
public sealed class InvalidGoogleTokenException : ApiException
{
    public InvalidGoogleTokenException()
        : base(401, "INVALID_GOOGLE_TOKEN", "Đăng nhập Google không hợp lệ hoặc đã hết hạn. Vui lòng thử lại.")
    {
    }
}
