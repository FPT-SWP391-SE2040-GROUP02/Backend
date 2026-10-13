using Google.Apis.Auth;
using LegacyVault.Application.Security;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Kiểm tra Google ID token bằng thư viện chính thức Google.Apis.Auth.
/// </summary>
/// <remarks>
/// GoogleJsonWebSignature.ValidateAsync tự kiểm tra: chữ ký theo khóa công khai của Google (tự tải và cache),
/// iss là accounts.google.com, exp còn hạn, và aud phải là Client ID của app (truyền qua Audience).
/// Nonce thì thư viện không kiểm tra nên tự so sánh ở dưới.
/// </remarks>
public sealed class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly GoogleAuthSettings _settings;

    public GoogleTokenVerifier(IOptions<GoogleAuthSettings> options)
    {
        _settings = options.Value;
    }

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, string? expectedNonce, CancellationToken ct)
    {
        // Thiếu Client ID là lỗi cấu hình của server, không phải lỗi của người dùng → báo rõ để dev sửa.
        if (string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            throw new InvalidOperationException("Chưa cấu hình GoogleAuth:ClientId.");
        }

        var validationSettings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { _settings.ClientId },
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);
        }
        catch (InvalidJwtException)
        {
            // Sai chữ ký, sai aud/iss, hết hạn hoặc chuỗi không phải JWT.
            return null;
        }

        // FE có gửi nonce thì token phải chứa đúng nonce đó (chống dùng lại token cũ).
        if (!string.IsNullOrEmpty(expectedNonce) && payload.Nonce != expectedNonce)
        {
            return null;
        }

        return new GoogleIdentity
        {
            Subject = payload.Subject,
            Email = payload.Email ?? string.Empty,
            EmailVerified = payload.EmailVerified,
            FullName = payload.Name,
        };
    }
}
