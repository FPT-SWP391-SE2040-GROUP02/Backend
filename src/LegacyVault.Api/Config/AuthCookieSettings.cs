namespace LegacyVault.Api.Config;

/// <summary>
/// Cấu hình cookie phiên (refresh token và antiforgery) theo môi trường.
/// </summary>
/// <remarks>
/// Cookie phiên dùng SameSite=Lax khi FE và API cùng site (local: localhost:5173 → localhost:5000).
/// Khi deploy tách domain (Vercel ↔ Render) thì request là cross-site: browser KHÔNG gửi cookie
/// SameSite=Lax, nên phiên đăng nhập không giữ được. Khi đó phải đặt SameSite=None + Secure,
/// nghĩa là trình duyệt chỉ chấp nhận cookie đó qua HTTPS.
/// Bật bằng biến môi trường <c>Auth__CrossSiteCookies=true</c> (Render) — xem docs/deployment.
/// </remarks>
public sealed class AuthCookieSettings
{
    public const string SectionName = "Auth";

    /// <summary>true khi FE và API khác site (Vercel ↔ Render): cookie dùng SameSite=None; Secure.</summary>
    public bool CrossSiteCookies { get; set; }
}
