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
    [EndpointDescription("Cần CSRF proof. Gửi GIS credential dưới tên idToken (≤4096 ký tự), không dùng OAuth access token. nonce optional ≤256, nếu gửi phải khớp nonce đã truyền GIS; returnUrl optional ≤2048 chỉ chấp nhận đường dẫn nội bộ an toàn. signedIn=true: session chứa AuthSessionResponse và cấp cookie lv_refresh; accountCreated cho biết user mới. signedIn=false: session=null, có linkTicket và maskedEmail; chưa cấp refresh cookie mới. Email trùng không tự gộp tài khoản: gọi link-confirm bằng mật khẩu app. Lỗi 401 INVALID_GOOGLE_TOKEN, 403 ACCOUNT_NOT_ACTIVE; thiếu GoogleAuth:ClientId là lỗi cấu hình 500. Rate limit chung email/Google/link-confirm: 5 request/phút/IP. CÁCH TEST TRÊN SWAGGER (liên kết Google vào tài khoản đăng ký bằng email + mật khẩu): điều kiện là phải có sẵn một tài khoản đăng ký bằng email + mật khẩu (email trùng email Google) và tài khoản đó chưa liên kết Google. Bước 1: gọi GET /api/v1/auth/csrf, sao chép requestToken dán vào header X-CSRF-Token. Bước 2: trên web (frontend) mở F12, tab Network, bấm Đăng nhập bằng Google, tìm request POST auth/google rồi sao chép idToken, nonce và returnUrl trong Payload. Bước 3: dán idToken/nonce/returnUrl vào body của API này (xóa các giá trị mẫu \"string\" nếu không dùng). Kết quả signedIn=false kèm linkTicket (hết hạn sau 10 phút, dùng một lần). Bước 4: sao chép linkTicket sang POST /api/v1/auth/google/link-confirm cùng mật khẩu app để hoàn tất liên kết. idToken của Google chỉ dùng được một thời gian ngắn, lấy xong nên dán ngay.")]
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
    [EndpointDescription("Chỉ gọi sau google trả signedIn=false. Cần CSRF proof; linkTicket ≤256 ký tự, sống 10 phút, dùng một lần; password app bắt buộc ≤128. Trả raw AuthSessionResponse và cookie lv_refresh; không trả GoogleLoginResponse. Giữ ticket/returnUrl đã lọc trong RAM, không URL/storage/log. Lỗi 400 INVALID_LINK_TICKET, 401 INVALID_CREDENTIALS, 423 ACCOUNT_LOCKED, 403 ACCOUNT_NOT_ACTIVE, 409 GOOGLE_ACCOUNT_ALREADY_LINKED. Vé hết hạn thì bắt đầu Google lại; không tự retry. Rate limit chung email/Google/link-confirm: 5 request/phút/IP. CÁCH TEST TRÊN SWAGGER: API này là bước 2 của luồng liên kết Google; chỉ dùng được khi tài khoản app được đăng ký bằng email + mật khẩu và chưa liên kết Google. Lấy X-CSRF-Token từ GET /api/v1/auth/csrf, lấy linkTicket từ phản hồi signedIn=false của POST /api/v1/auth/google (xem cách lấy idToken/nonce/returnUrl ở API đó), rồi nhập linkTicket cùng mật khẩu app của tài khoản. Thành công trả 200 AuthSessionResponse và cookie lv_refresh.")]
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

        if (result.TwoFactor != null)
        {
            return Ok(result.TwoFactor);
        }

        _cookieWriter.Append(Response, result);
        return Ok(result.Session);
    }
}
