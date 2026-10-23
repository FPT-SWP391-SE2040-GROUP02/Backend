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
[Tags("Auth")]
[Route("api/v1/auth")]
[Route("v1/auth")]
// Response chứa token phiên hoặc thông tin tài khoản nên cấm trình duyệt và proxy lưu đệm.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IRegistrationService _registrationService;
    private readonly AuthCookieWriter _cookieWriter;
    private readonly IPasswordResetService _passwordResetService;

    public AuthController(
        IAuthService authService,
        IRegistrationService registrationService,
        AuthCookieWriter cookieWriter,
        IPasswordResetService passwordResetService)
    {
        _authService = authService;
        _registrationService = registrationService;
        _cookieWriter = cookieWriter;
        _passwordResetService = passwordResetService;
    }

    /// <summary>Cấp proof antiforgery theo principal hiện tại; không trả auth secrets.</summary>
    [HttpGet(AuthCsrfContract.TokenRoute)]
    [EndpointSummary("Lấy CSRF proof trước thao tác đăng nhập/phiên")]
    [EndpointDescription("Chạy trước login, refresh, logout, google hoặc google/link-confirm. Giữ cookie lv_antiforgery do trình duyệt nhận; copy requestToken vào X-CSRF-Token. GET và POST phải dùng cùng Bearer principal; lấy proof mới sau đổi danh tính. Response no-store, không cấp phiên đăng nhập.")]
    [ProducesResponseType<AuthCsrfTokenDto>(StatusCodes.Status200OK)]
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
    [EndpointSummary("Đăng ký tài khoản bằng email")]
    [EndpointDescription("Email hợp lệ ≤256 ký tự; password 8–128; confirmPassword phải khớp; fullName bắt buộc ≤200. Trả 202 không body kể cả email đã tồn tại để tránh dò tài khoản. Chưa cấp access token/cookie; không bảo đảm email đã giao. Lỗi 400 VALIDATION_ERROR.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
    [EndpointSummary("Xác minh email bằng token trong liên kết")]
    [EndpointDescription("Token opaque ≤256 ký tự, sống 24 giờ và dùng một lần. Trả raw UserResponse, không tự đăng nhập hoặc cấp refresh cookie. Lỗi 400 VALIDATION_ERROR hoặc INVALID_VERIFICATION_TOKEN khi token sai, hết hạn hoặc đã dùng.")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
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
    [EndpointSummary("Gửi lại email xác minh")]
    [EndpointDescription("Nhận email hợp lệ ≤256 ký tự. Trả 202 không body cả khi email không tồn tại, đã xác minh hoặc tài khoản không active; không tiết lộ tình trạng tài khoản. Khi cấp token mới sẽ vô hiệu token cũ. Lỗi 400 VALIDATION_ERROR.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
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
    [EndpointSummary("Đăng nhập bằng email và mật khẩu")]
    [EndpointDescription("Cần CSRF proof và cookie antiforgery từ GET /api/v1/auth/csrf. Trả raw AuthSessionResponse: accessToken chỉ giữ RAM; refresh token chỉ nằm cookie HttpOnly lv_refresh. rememberMe chỉ quyết định cookie persistent; idle 30 phút, tối đa 12 giờ. Admin đã bật xác thực hai bước (api-contract 6.2a) nhận 200 TwoFactorChallengeResponse {twoFactorRequired:true, challengeToken, expiresIn} thay cho phiên, không có cookie; gọi POST /auth/2fa/verify để hoàn tất. Lỗi 401 INVALID_CREDENTIALS, 403 ACCOUNT_NOT_ACTIVE, 423 ACCOUNT_LOCKED; 5 lần sai trong 15 phút khóa 15 phút. Rate limit chung email/Google/link-confirm: 5 request/phút/IP.")]
    [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var ipRange = ClientIpRange.From(HttpContext);
        var result = await _authService.LoginAsync(request, ipRange, ct);

        // Admin đã bật xác thực hai bước: chưa có phiên, không đặt cookie; FE gọi POST /auth/2fa/verify.
        if (result.TwoFactor != null)
        {
            return Ok(result.TwoFactor);
        }

        _cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }

    /// <summary>
    /// POST /v1/auth/refresh — Đổi refresh token trong cookie lấy access token mới.
    /// Trả 200 AuthSessionResponse kèm cookie mới (api-contract §6.2).
    /// </summary>
    [HttpPost("refresh")]
    [EndpointSummary("Xoay refresh cookie và cấp access token mới")]
    [EndpointDescription("Không body. Cần cookie HttpOnly lv_refresh và CSRF proof cùng principal. Trả raw AuthSessionResponse và refresh cookie mới. Token cũ bị thu hồi; dùng lại token cũ thu hồi cả family. Không gọi song song hoặc tự retry. 401 SESSION_EXPIRED xóa refresh cookie. Idle 30 phút từ lần cấp/refresh, tối đa 12 giờ kể từ login. Rate limit 30 request/phút/IP.")]
    [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
    [EndpointSummary("Đăng xuất thiết bị hiện tại hoặc tất cả thiết bị")]
    [EndpointDescription("Cần CSRF proof. Body tùy chọn, revokeAllDevices mặc định false. Logout-all cần Bearer principal active hoặc refresh token active; không nhận cookie thuộc user khác. Thành công 204 không body và xóa cookie lv_refresh; FE sau đó mới xóa RAM/cache. Thu hồi refresh không vô hiệu access JWT còn hạn. 401 SESSION_EXPIRED khi logout-all thiếu quyền; rate limit 30 request/phút/IP.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
    [EndpointSummary("Đọc thông tin người dùng từ Bearer access token")]
    [EndpointDescription("Dùng Authorize với accessToken. Trả raw UserResponse; không đọc refresh cookie và không tạo token mới. Khi reload mất access token RAM, gọi csrf rồi refresh trước. 401 SESSION_EXPIRED khi token sai/hết hạn hoặc user không còn active.")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [Authorize]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        var user = await _authService.GetCurrentUserAsync(ct);
        return Ok(user);
    }

    /// <summary>
    /// POST /api/v1/auth/forgot-password — Gửi link đặt lại mật khẩu qua email.
    /// Luôn trả 202 Accepted, không body, để không lộ email nào đã có tài khoản.
    /// </summary>
    [HttpPost("forgot-password")]
    [EndpointSummary("Quên mật khẩu: gửi link đặt lại qua email")]
    [EndpointDescription("Email hợp lệ ≤256 ký tự. Trả 202 không body cả khi email không tồn tại, tài khoản bị khóa hoặc chỉ dùng Google. Link hạn 30 phút, dùng một lần; yêu cầu mới vô hiệu link cũ; trong 60 giây kể từ link gần nhất thì không gửi thêm. Lỗi 400 VALIDATION_ERROR. Rate limit 5 request/phút/IP.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(AuthRateLimitPolicies.PasswordReset)]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request,
        [FromServices] ILogger<AuthController> logger, CancellationToken ct)
    {
        try
        {
            await _passwordResetService.ForgotPasswordAsync(request, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // SMTP fails only for existing accounts. Keep the same public acknowledgement,
            // otherwise a delivery outage exposes which emails have a password account.
            // ponytail: no recovery-mail outbox yet; add one when reliable delivery retries are required.
            logger.LogWarning(exception, "Password recovery request failed with {ExceptionType} ({Message}); delivery not confirmed.",
                exception.GetType().Name, exception.Message);
        }
        return Accepted();
    }

    /// <summary>
    /// POST /api/v1/auth/reset-password — Đặt mật khẩu mới bằng token trong link.
    /// Trả 204 và đăng xuất mọi thiết bị của tài khoản.
    /// </summary>
    [HttpPost("reset-password")]
    [EndpointSummary("Đặt mật khẩu mới bằng token trong link email")]
    [EndpointDescription("Token opaque ≤256 ký tự lấy từ link; newPassword 8–128; confirmPassword phải khớp. Thành công trả 204 không body, không tự đăng nhập; mọi refresh token của tài khoản bị thu hồi nên các thiết bị phải đăng nhập lại. Email chưa xác minh được coi là đã xác minh. Lỗi 400 VALIDATION_ERROR hoặc INVALID_RESET_TOKEN khi token sai, hết hạn, đã dùng hoặc tài khoản không còn hoạt động. Rate limit 5 request/phút/IP.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(AuthRateLimitPolicies.PasswordReset)]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _passwordResetService.ResetPasswordAsync(request, ct);
        return NoContent();
    }
}
