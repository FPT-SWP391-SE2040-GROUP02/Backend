using System.Security.Claims;
using System.Text.Encodings.Web;
using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LegacyVault.Api.Security;

/// <summary>
/// XÁC THỰC TẠM cho môi trường dev khi chưa có đăng nhập: header <c>X-Dev-User-Id: {userId}</c>.
/// </summary>
/// <remarks>
/// - Chỉ xác thực khi DevAuth:Enabled = true, và AuthConfig chặn khởi động nếu bật ngoài Development.
/// - Không kiểm tra mật khẩu: chỉ để thử luồng kho/tài sản trước khi có auth.
///   Khi làm auth (JWT Bearer) thì XÓA handler này và đổi default scheme trong AuthConfig.
/// - Thiếu/sai header → 401 SESSION_EXPIRED dạng ProblemDetails, giống contract khi token hết hạn.
/// </remarks>
public sealed class DevHeaderAuthenticationHandler(
    IOptionsMonitor<DevHeaderAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<DevHeaderAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevHeader";
    public const string HeaderName = "X-Dev-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.Enabled
            || !Request.Headers.TryGetValue(HeaderName, out var value)
            || !Guid.TryParse(value.ToString(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        var problem = GlobalExceptionHandler.Problem(StatusCodes.Status401Unauthorized, ErrorCode.SessionExpired,
            "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
        problem.Extensions["traceId"] = Context.TraceIdentifier;
        await problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = Context, ProblemDetails = problem });
    }
}

public sealed class DevHeaderAuthenticationOptions : AuthenticationSchemeOptions
{
    public bool Enabled { get; set; }
}
