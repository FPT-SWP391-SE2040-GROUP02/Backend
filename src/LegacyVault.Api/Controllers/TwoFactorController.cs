using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Xác thực hai bước TOTP cho tài khoản Admin (api-contract mục 6.2a). Tách khỏi AuthController để không đụng
/// code đăng nhập thường.
/// </summary>
[ApiController]
[Route("api/v1/auth/2fa")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TwoFactorController : ControllerBase
{
    private readonly ITwoFactorService twoFactorService;
    private readonly AuthCookieWriter cookieWriter;

    public TwoFactorController(ITwoFactorService twoFactorService, AuthCookieWriter cookieWriter)
    {
        this.twoFactorService = twoFactorService;
        this.cookieWriter = cookieWriter;
    }

    /// <summary>Hoàn tất đăng nhập Admin bằng mã TOTP hoặc mã khôi phục; cấp phiên và cookie lv_refresh.</summary>
    [HttpPost("verify")]
    [Consumes("application/json")]
    [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [AllowAnonymous]
    public async Task<IActionResult> Verify(TwoFactorVerifyRequest request, CancellationToken ct)
    {
        AuthResult result = await twoFactorService.VerifyLoginAsync(request, ct);
        cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }

    /// <summary>Trạng thái xác thực hai bước của tài khoản Admin đang đăng nhập.</summary>
    [HttpGet("status")]
    [Authorize]
    [ProducesResponseType<TwoFactorStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<TwoFactorStatusResponse> GetStatus(CancellationToken ct)
    {
        return twoFactorService.GetStatusAsync(ct);
    }

    /// <summary>Tạo khóa TOTP mới; chỉ bắt buộc khi đăng nhập sau khi gọi enable với mã đầu tiên.</summary>
    [HttpPost("setup")]
    [Authorize]
    [EnableRateLimiting("two-factor")]
    [ProducesResponseType<TwoFactorSetupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TwoFactorSetupResponse> Setup(CancellationToken ct)
    {
        return twoFactorService.SetupAsync(ct);
    }

    /// <summary>Xác nhận mã đầu tiên để bật; trả mã khôi phục (chỉ hiển thị một lần).</summary>
    [HttpPost("enable")]
    [Authorize]
    [Consumes("application/json")]
    [EnableRateLimiting("two-factor")]
    [ProducesResponseType<TwoFactorEnableResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TwoFactorEnableResponse> Enable(TwoFactorCodeRequest request, CancellationToken ct)
    {
        return twoFactorService.EnableAsync(request, ct);
    }

    /// <summary>Tắt xác thực hai bước bằng mã TOTP hoặc mã khôi phục.</summary>
    [HttpPost("disable")]
    [Authorize]
    [Consumes("application/json")]
    [EnableRateLimiting("two-factor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Disable(TwoFactorCodeRequest request, CancellationToken ct)
    {
        await twoFactorService.DisableAsync(request, ct);
        return NoContent();
    }
}
