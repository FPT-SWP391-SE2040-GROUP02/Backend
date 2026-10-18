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
[Tags("Auth")]
[Route("api/v1/auth/google")]
[Route("v1/auth/google")]
// Response cấp token phiên như đăng nhập thường nên cấm trình duyệt và proxy lưu đệm.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
    [EndpointSummary("Đăng nhập Google hoặc nhận vé xác nhận liên kết")]
    [EndpointDescription("Cần CSRF proof. Gửi GIS credential dưới tên idToken (≤4096 ký tự), không dùng OAuth access token. nonce optional ≤256, nếu gửi phải khớp nonce đã truyền GIS; returnUrl optional ≤2048 chỉ chấp nhận đường dẫn nội bộ an toàn. signedIn=true: session chứa AuthSessionResponse và cấp cookie lv_refresh; accountCreated cho biết user mới. signedIn=false: session=null, có linkTicket và maskedEmail; chưa cấp refresh cookie mới. Email trùng không tự gộp tài khoản: gọi link-confirm bằng mật khẩu app. Lỗi 401 INVALID_GOOGLE_TOKEN, 403 ACCOUNT_NOT_ACTIVE; thiếu GoogleAuth:ClientId là lỗi cấu hình 500. Rate limit chung email/Google/link-confirm: 5 request/phút/IP.")]
    [ProducesResponseType<GoogleLoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
    [EndpointSummary("Xác nhận liên kết Google bằng mật khẩu app rồi đăng nhập")]
    [EndpointDescription("Chỉ gọi sau google trả signedIn=false. Cần CSRF proof; linkTicket ≤256 ký tự, sống 10 phút, dùng một lần; password app bắt buộc ≤128. Trả raw AuthSessionResponse và cookie lv_refresh; không trả GoogleLoginResponse. Giữ ticket/returnUrl đã lọc trong RAM, không URL/storage/log. Lỗi 400 INVALID_LINK_TICKET, 401 INVALID_CREDENTIALS, 423 ACCOUNT_LOCKED, 403 ACCOUNT_NOT_ACTIVE, 409 GOOGLE_ACCOUNT_ALREADY_LINKED. Vé hết hạn thì bắt đầu Google lại; không tự retry. Rate limit chung email/Google/link-confirm: 5 request/phút/IP.")]
    [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
