using LegacyVault.Application.Dtos.Responses;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace LegacyVault.Api.Security;

/// <summary>Ghi và xóa cookie chứa refresh token sau đăng nhập, làm mới phiên và đăng xuất.</summary>
public sealed class AuthCookieWriter
{
    public const string RefreshTokenCookieName = "lv_refresh";

    // Trình duyệt chỉ gửi cookie này tới các API đăng nhập, không gửi kèm mọi request khác.
    public const string CookiePath = "/api/v1/auth";

    private readonly IWebHostEnvironment _environment;

    public AuthCookieWriter(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public void Append(HttpResponse response, AuthResult result)
    {
        // HttpOnly: JavaScript không đọc được token. Lax: hạn chế gửi cookie khi request đến từ trang khác.
        // Secure tắt ở môi trường dev vì chạy http://localhost.
        var options = new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = CookiePath,
            Secure = !_environment.IsDevelopment(),
        };

        // "Ghi nhớ đăng nhập": cookie có hạn nên còn sau khi đóng trình duyệt; không chọn thì mất khi đóng trình duyệt.
        if (result.IsPersistent)
        {
            options.Expires = result.RefreshTokenExpiresAt;
        }

        response.Cookies.Append(RefreshTokenCookieName, result.RefreshToken, options);
    }

    /// <summary>Xóa cookie khi đăng xuất hoặc phiên hết hạn. Phải cùng Path thì trình duyệt mới xóa đúng cookie.</summary>
    public void Clear(HttpResponse response)
    {
        response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            Path = CookiePath,
        });
    }
}
