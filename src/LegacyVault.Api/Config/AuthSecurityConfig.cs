using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using LegacyVault.Api.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LegacyVault.Api.Config;

/// <summary>Antiforgery và rate limit Auth chuyển từ repository test, giữ response raw của Backend.</summary>
public static class AuthSecurityConfig
{
    public static IServiceCollection AddAuthSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAntiforgery();
        services.Configure<AuthCookieSettings>(configuration.GetSection(AuthCookieSettings.SectionName));
        services.AddOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                options.HeaderName = AuthCsrfContract.HeaderName;
                options.Cookie.Name = AuthCsrfContract.CookieName;
                options.Cookie.Path = AuthCsrfContract.CookiePath;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                if (environment.IsDevelopment())
                {
                    options.Cookie.SecurePolicy = CookieSecurePolicy.None;
                }
            })
            .Configure<IOptions<AuthCookieSettings>>((options, cookieSettings) =>
            {
                if (cookieSettings.Value.CrossSiteCookies)
                {
                    options.Cookie.SameSite = SameSiteMode.None;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                }
            });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    int seconds = checked((int)Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));
                    context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };
            var policies = new[]
            {
                (AuthRateLimitPolicies.Login, AuthRateLimitPolicies.LoginPermitLimit),
                (AuthRateLimitPolicies.Refresh, AuthRateLimitPolicies.SessionPermitLimit),
                (AuthRateLimitPolicies.Logout, AuthRateLimitPolicies.SessionPermitLimit),
                ("plan-setup", 10),
                ("executor-invitations", 30),
                ("login-methods", 10),
                ("two-factor", 5),
                (AuthRateLimitPolicies.PasswordReset, AuthRateLimitPolicies.PasswordResetPermitLimit),
                (AuthRateLimitPolicies.Register, AuthRateLimitPolicies.RegisterPermitLimit),
                (AuthRateLimitPolicies.GoogleNonce, AuthRateLimitPolicies.GoogleNoncePermitLimit),
                // R5 (audit 2026-10-27): giới hạn kênh điểm danh DMS (web), chống flood.
                ("check-ins", 10),
                // Mã PIN kho: đặt/đổi/quên PIN. Quên PIN kiểm mật khẩu tài khoản nên cần giới hạn dò.
                ("vault-pin", 10),
                // Admin thử kết nối dịch vụ ngoài bằng giá trị đang nhập: mỗi lần là một lời gọi thật ra ngoài.
                ("system-config-test", 10),
            };
            foreach (var (name, limit) in policies)
            {
                options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.MapToIPv6() ?? IPAddress.IPv6None,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limit,
                        Window = AuthRateLimitPolicies.Window,
                        QueueLimit = AuthRateLimitPolicies.QueueLimit,
                        AutoReplenishment = true,
                    }));
            }
            // eKYC chạy OCR và nhận diện khuôn mặt trên CPU nên giới hạn theo người dùng (IP chỉ là phương án dự phòng
            // khi không có claim), tránh một tài khoản làm nghẽn dịch vụ dùng chung.
            AddPerUserPolicy(options, AuthRateLimitPolicies.Ekyc, AuthRateLimitPolicies.EkycPermitLimit);
            AddPerUserPolicy(options, AuthRateLimitPolicies.EkycFrame, AuthRateLimitPolicies.EkycFramePermitLimit);
        });
        return services;
    }

    private static void AddPerUserPolicy(RateLimiterOptions options, string name, int limit)
    {
        options.AddPolicy(name, context =>
        {
            string? userId = context.User.FindFirst("sub")?.Value;
            string key = "ip:" + context.Connection.RemoteIpAddress;
            if (!string.IsNullOrEmpty(userId))
            {
                key = "user:" + userId;
            }
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = AuthRateLimitPolicies.Window,
                QueueLimit = AuthRateLimitPolicies.QueueLimit,
                AutoReplenishment = true,
            });
        });
    }
}
