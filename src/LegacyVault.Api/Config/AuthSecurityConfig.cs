using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using LegacyVault.Api.Security;

namespace LegacyVault.Api.Config;

/// <summary>Antiforgery và rate limit Auth chuyển từ repository test, giữ response raw của Backend.</summary>
public static class AuthSecurityConfig
{
    public static IServiceCollection AddAuthSecurity(this IServiceCollection services)
    {
        services.AddAntiforgery();
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
                (AuthRateLimitPolicies.PasswordReset, AuthRateLimitPolicies.PasswordResetPermitLimit),
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
        });
        return services;
    }
}
