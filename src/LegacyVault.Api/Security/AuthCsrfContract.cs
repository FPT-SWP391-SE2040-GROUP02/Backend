namespace LegacyVault.Api.Security;

/// <summary>Hợp đồng antiforgery chuyển từ test; bảo vệ được đăng ký tại AuthSecurityConfig.</summary>
public static class AuthCsrfContract
{
    /// <summary>Relative GET route under the existing auth controller.</summary>
    public const string TokenRoute = "csrf";

    /// <summary>Request header carrying the antiforgery request token, never an access JWT.</summary>
    public const string HeaderName = "X-CSRF-Token";

    /// <summary>HttpOnly cookie owned by ASP.NET Core antiforgery.</summary>
    public const string CookieName = "lv_antiforgery";

    /// <summary>Cookie scope shared by token issuance and protected API mutations.</summary>
    public const string CookiePath = "/api/v1";
}

/// <summary>GET /api/v1/auth/csrf trả raw requestToken theo convention Backend.</summary>
/// <param name="RequestToken">Antiforgery request token held only in client RAM.</param>
public sealed record AuthCsrfTokenDto(string RequestToken);
