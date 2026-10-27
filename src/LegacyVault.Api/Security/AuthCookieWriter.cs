using LegacyVault.Api.Config;
using LegacyVault.Application.Dtos.Responses;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LegacyVault.Api.Security;

public sealed class AuthCookieWriter
{
    public const string RefreshTokenCookieName = "lv_refresh";
    public const string CookiePath = "/api/v1/auth";

    private readonly IWebHostEnvironment _environment;
    private readonly AuthCookieSettings _cookieSettings;

    public AuthCookieWriter(IWebHostEnvironment environment, IOptions<AuthCookieSettings> cookieSettings)
    {
        _environment = environment;
        _cookieSettings = cookieSettings.Value;
    }

    public void Append(HttpResponse response, AuthResult result)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            // R4 (audit 2026-10-27): chính sách cookie đồng bộ hai chiều —
            // cùng domain: Strict (siết nhất, chặn hoàn toàn gửi cookie cross-site);
            // cross-site (Vercel/Render dev split): None + Secure.
            SameSite = _cookieSettings.CrossSiteCookies ? SameSiteMode.None : SameSiteMode.Strict,
            Path = CookiePath,
            // SameSite=None bắt buộc Secure; local (http) không dùng chế độ cross-site nên vẫn cho phép http.
            Secure = !_environment.IsDevelopment() || _cookieSettings.CrossSiteCookies,
        };

        if (result.IsPersistent)
        {
            options.Expires = result.RefreshTokenExpiresAt;
        }

        response.Cookies.Append(RefreshTokenCookieName, result.RefreshToken, options);
    }

    public void Clear(HttpResponse response)
    {
        response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            Path = CookiePath,
            SameSite = _cookieSettings.CrossSiteCookies ? SameSiteMode.None : SameSiteMode.Strict,
            Secure = !_environment.IsDevelopment() || _cookieSettings.CrossSiteCookies,
        });
    }
}
