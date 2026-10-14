using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LegacyVault.Api.Controllers;

[ApiController]
[Produces("application/json")]
[Route("api/v1/auth")]
[Route("v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRegistrationService _registrationService;
    private readonly AuthCookieWriter _cookieWriter;

    public AuthController(
        IAuthService authService,
        IRegistrationService registrationService,
        AuthCookieWriter cookieWriter)
    {
        _authService = authService;
        _registrationService = registrationService;
        _cookieWriter = cookieWriter;
    }

    /// <summary>Cấp proof antiforgery theo principal hiện tại; không trả auth secrets.</summary>
    [HttpGet(AuthCsrfContract.TokenRoute)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (tokens.RequestToken == null)
        {
            throw new InvalidOperationException("Không cấp được antiforgery proof.");
        }
        return Ok(new AuthCsrfTokenDto(tokens.RequestToken));
    }

    /// <summary>
    /// POST /v1/auth/register — Đăng ký tài khoản mới và gửi link xác minh email.
    /// Luôn trả 202 Accepted, không body (api-contract §6.1).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await _registrationService.RegisterAsync(request, ct);
        return Accepted();
    }

    /// <summary>
    /// POST /v1/auth/verify-email — Xác minh email bằng token trong link.
    /// Trả 200 UserResponse (api-contract §6.1).
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        var user = await _registrationService.VerifyEmailAsync(request, ct);
        return Ok(user);
    }

    /// <summary>
    /// POST /v1/auth/resend-verification — Gửi lại link xác minh email.
    /// Luôn trả 202 Accepted, không body (api-contract §6.1).
    /// </summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken ct)
    {
        await _registrationService.ResendVerificationAsync(request, ct);
        return Accepted();
    }

    /// <summary>
    /// POST /v1/auth/login — Đăng nhập bằng email và mật khẩu.
    /// Trả 200 AuthSessionResponse kèm Set-Cookie lv_refresh (api-contract §6.2).
    /// </summary>
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var ipRange = ClientIpRange.From(HttpContext);
        var result = await _authService.LoginAsync(request, ipRange, ct);

        _cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }

    /// <summary>
    /// POST /v1/auth/refresh — Đổi refresh token trong cookie lấy access token mới.
    /// Trả 200 AuthSessionResponse kèm cookie mới (api-contract §6.2).
    /// </summary>
    [HttpPost("refresh")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Refresh)]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieWriter.RefreshTokenCookieName];

        AuthResult result;
        try
        {
            result = await _authService.RefreshAsync(refreshToken, ct);
        }
        catch (SessionExpiredException)
        {
            _cookieWriter.Clear(Response);
            throw;
        }

        _cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }

    /// <summary>
    /// POST /v1/auth/logout — Đăng xuất và xóa cookie lv_refresh.
    /// Trả 204 sau thu hồi; logout-all thiếu bằng chứng phiên trả 401.
    /// </summary>
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Logout)]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request,
        CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieWriter.RefreshTokenCookieName];
        var revokeAllDevices = request?.RevokeAllDevices ?? false;

        await _authService.LogoutAsync(refreshToken, revokeAllDevices, ct);
        _cookieWriter.Clear(Response);
        return NoContent();
    }

    /// <summary>
    /// GET /v1/auth/session — Lấy thông tin người dùng đang đăng nhập qua Bearer token.
    /// Trả 200 UserResponse (api-contract §6.2).
    /// </summary>
    [HttpGet("session")]
    [Authorize]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        var user = await _authService.GetCurrentUserAsync(ct);
        return Ok(user);
    }

}
