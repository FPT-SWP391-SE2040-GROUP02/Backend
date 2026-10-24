using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>Phương thức đăng nhập của người đang đăng nhập: mật khẩu app và Google (AUTH-03, api-contract mục 6.4).</summary>
[ApiController]
[Authorize]
[Route("api/v1/me/login-methods")]
[Produces("application/json")]
[EnableRateLimiting("login-methods")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LoginMethodsController : ControllerBase
{
    private readonly ILoginMethodService loginMethodService;

    public LoginMethodsController(ILoginMethodService loginMethodService)
    {
        this.loginMethodService = loginMethodService;
    }

    /// <summary>Danh sách phương thức đăng nhập hiện có.</summary>
    [HttpGet]
    [ProducesResponseType<List<LoginMethodResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<List<LoginMethodResponse>> GetLoginMethods(CancellationToken ct)
    {
        return loginMethodService.GetMyLoginMethodsAsync(ct);
    }

    /// <summary>Kiểm tra mật khẩu hiện tại, không đổi dữ liệu; dùng để mở bước chọn Google khi liên kết.</summary>
    [HttpPost("password/verify")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyPassword(VerifyPasswordRequest request, CancellationToken ct)
    {
        await loginMethodService.VerifyPasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>Thêm Google vào tài khoản; xác thực lại bằng mật khẩu hiện tại.</summary>
    [HttpPost("google")]
    [Consumes("application/json")]
    [ProducesResponseType<LoginMethodResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LoginMethodResponse>> LinkGoogle(LinkGoogleRequest request, CancellationToken ct)
    {
        LoginMethodResponse created = await loginMethodService.LinkGoogleAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Bỏ liên kết Google; chưa liên kết thì vẫn thành công.</summary>
    [HttpPost("google/unlink")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkGoogle(UnlinkGoogleRequest request, CancellationToken ct)
    {
        await loginMethodService.UnlinkGoogleAsync(request, ct);
        return NoContent();
    }

    /// <summary>Tài khoản chỉ có Google đặt thêm mật khẩu app.</summary>
    [HttpPost("password")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPassword(SetPasswordRequest request, CancellationToken ct)
    {
        await loginMethodService.SetPasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>Bỏ mật khẩu app, chỉ còn Google.</summary>
    [HttpPost("password/remove")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemovePassword(RemovePasswordRequest request, CancellationToken ct)
    {
        await loginMethodService.RemovePasswordAsync(request, ct);
        return NoContent();
    }
}
