using System.Net;
using System.Net.Sockets;
using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
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
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var ipRange = GetClientIpRange();
        var result = await _authService.LoginAsync(request, ipRange, ct);

        _cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }

    /// <summary>
    /// POST /v1/auth/refresh — Đổi refresh token trong cookie lấy access token mới.
    /// Trả 200 AuthSessionResponse kèm cookie mới (api-contract §6.2).
    /// </summary>
    [HttpPost("refresh")]
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
    /// Luôn trả 204 No Content (api-contract §6.2).
    /// </summary>
    [HttpPost("logout")]
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

    private string GetClientIpRange()
    {
        var ip = HttpContext.Connection.RemoteIpAddress;

        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedValues))
        {
            var headerValue = forwardedValues.ToString();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                var firstIpStr = headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
                if (IPAddress.TryParse(firstIpStr, out var parsedIp))
                {
                    ip = parsedIp;
                }
            }
        }

        if (ip == null)
        {
            return "unknown";
        }

        try
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = ip.GetAddressBytes();
                var prefixBytes = new byte[16];
                Array.Copy(bytes, prefixBytes, 8);
                var subnet = new IPAddress(prefixBytes);
                return $"{subnet}/64";
            }
        }
        catch
        {
            // Tránh ngắt request khi parse IP
        }

        return "unknown";
    }
}
