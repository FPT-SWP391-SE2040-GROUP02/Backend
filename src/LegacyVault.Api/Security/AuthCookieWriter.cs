using LegacyVault.Application.Dtos.Responses;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace LegacyVault.Api.Security;

public sealed class AuthCookieWriter
{
    public const string RefreshTokenCookieName = "lv_refresh";
    public const string CookiePath = "/api/v1/auth";

    private readonly IWebHostEnvironment _environment;

    public AuthCookieWriter(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public void Append(HttpResponse response, AuthResult result)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = CookiePath,
            Secure = !_environment.IsDevelopment(),
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
        });
    }
}
