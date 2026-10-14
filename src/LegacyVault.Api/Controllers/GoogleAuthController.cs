using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Infrastructure.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Đăng nhập bằng Google (api-contract §6.3). Tách khỏi AuthController để không đụng code đăng nhập thường.
/// </summary>
[ApiController]
[Produces("application/json")]
[Route("api/v1/auth/google")]
[Route("v1/auth/google")]
public sealed class GoogleAuthController : ControllerBase
{
    private readonly IGoogleAuthService _googleAuthService;
    private readonly AuthCookieWriter _cookieWriter;

    public GoogleAuthController(IGoogleAuthService googleAuthService, AuthCookieWriter cookieWriter)
    {
        _googleAuthService = googleAuthService;
        _cookieWriter = cookieWriter;
    }

    /// <summary>
    /// POST /v1/auth/google — FE gửi Google ID token lấy từ Google Identity Services.
    /// Trả 200 GoogleLoginResponse: signedIn = true (kèm cookie lv_refresh) hoặc signedIn = false (cần liên kết).
    /// </summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [AllowAnonymous]
    public async Task<IActionResult> Login(GoogleLoginRequest request, CancellationToken ct)
    {
        var result = await _googleAuthService.LoginWithGoogleAsync(request, ct);

        // Chỉ ghi cookie refresh khi thật sự đăng nhập; trường hợp cần liên kết thì chưa có phiên.
        if (result.SignedIn && result.Auth != null)
        {
            _cookieWriter.Append(Response, result.Auth);
        }

        return Ok(GoogleLoginResponse.From(result));
    }

    /// <summary>
    /// POST /v1/auth/google/link-confirm — Nhập mật khẩu tài khoản app để liên kết Google rồi đăng nhập.
    /// Trả 200 AuthSessionResponse kèm cookie lv_refresh.
    /// </summary>
    [HttpPost("link-confirm")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmLink(ConfirmGoogleLinkRequest request, CancellationToken ct)
    {
        var ipRange = ClientIpRange.From(HttpContext);
        var result = await _googleAuthService.ConfirmGoogleLinkAsync(request, ipRange, ct);

        _cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }
}
