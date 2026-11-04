using System.Security.Claims;
using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Security;

/// <summary>
/// Chặn API khi hệ thống đang bảo trì (Admin bật Features.MaintenanceMode): chỉ tài khoản Quản trị viên dùng được,
/// mọi request khác nhận 503 MAINTENANCE_MODE để giao diện chuyển sang màn hình bảo trì.
/// </summary>
/// <remarks>
/// - Chạy sau UseAuthentication để biết vai trò của Bearer token (claim "role" = "ADMIN").
/// - Luôn cho qua các đường không phải API (health, OpenAPI), cờ công khai để giao diện biết đang bảo trì,
///   và các bước đăng nhập bằng email, mật khẩu (kể cả xác thực hai bước). Chính các service đăng nhập chặn tài khoản
///   không phải Admin, vì trước khi đăng nhập chưa biết vai trò.
/// - Đăng nhập Google (/auth/google, /auth/google/link-confirm) bị đóng hẳn khi bảo trì: tài khoản Admin chỉ tạo
///   trực tiếp trên hệ thống và đăng nhập bằng email, mật khẩu; đóng ở đây để người mới không được tạo tài khoản
///   Google trong lúc bảo trì. Hết bảo trì thì Google chạy như cũ.
/// - Cho qua webhook SePay để không mất giao dịch ngân hàng báo về trong lúc bảo trì.
/// - Ghi ProblemDetails trực tiếp, không ném exception, để không ghi log lỗi cho mỗi request bị chặn.
/// - Không đọc được cờ (database dừng) thì cho qua: bảo trì không thay cho xác thực và phân quyền ở endpoint.
/// </remarks>
public sealed class MaintenanceModeMiddleware
{
    private const string AdminRoleClaimValue = "ADMIN";

    /// <summary>Đường API luôn được qua dù đang bảo trì (so khớp không phân biệt hoa thường, cả tiền tố /api/v1 và /v1).</summary>
    private static readonly string[] AlwaysAllowedPaths = new string[]
    {
        "/v1/settings/public",
        "/v1/auth/csrf",
        "/v1/auth/login",
        "/v1/auth/refresh",
        "/v1/auth/logout",
        "/v1/auth/2fa/verify",
        "/v1/billing/sepay/webhook",
    };

    private readonly RequestDelegate next;

    public MaintenanceModeMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, MaintenanceModeGuard guard)
    {
        if (!IsApiPath(context.Request.Path) || IsAlwaysAllowed(context.Request.Path) || IsAdmin(context))
        {
            await next(context);
            return;
        }

        bool maintenanceOn = await ReadMaintenanceFlagAsync(context, guard);
        if (!maintenanceOn)
        {
            await next(context);
            return;
        }

        await WriteBlockedAsync(context);
    }

    /// <summary>
    /// Đọc cờ bảo trì. Không đọc được (ví dụ database đang dừng) thì coi như không bảo trì để request đi tiếp
    /// và endpoint tự trả lỗi đúng của nó (401, 404, 503 health...), thay vì mọi API cùng thành 500.
    /// </summary>
    private static async Task<bool> ReadMaintenanceFlagAsync(HttpContext context, MaintenanceModeGuard guard)
    {
        try
        {
            return await guard.IsOnAsync(context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ILogger<MaintenanceModeMiddleware> logger =
                context.RequestServices.GetRequiredService<ILogger<MaintenanceModeMiddleware>>();
            logger.LogDebug("Không đọc được cờ bảo trì ({ExceptionType}); cho request đi tiếp.",
                exception.GetType().Name);
            return false;
        }
    }

    /// <summary>Chỉ chặn đường API; trang health, OpenAPI và Swagger vẫn chạy để theo dõi hệ thống.</summary>
    private static bool IsApiPath(PathString path)
    {
        if (path.StartsWithSegments("/api"))
        {
            return true;
        }
        if (path.StartsWithSegments("/v1"))
        {
            return true;
        }
        return false;
    }

    private static bool IsAlwaysAllowed(PathString path)
    {
        string value = path.Value ?? string.Empty;
        if (value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("/api".Length);
        }
        value = value.TrimEnd('/');

        foreach (string allowed in AlwaysAllowedPaths)
        {
            if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAdmin(HttpContext context)
    {
        if (context.User.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            return false;
        }
        Claim? role = context.User.FindFirst("role");
        if (role == null)
        {
            return false;
        }
        return role.Value == AdminRoleClaimValue;
    }

    private static async Task WriteBlockedAsync(HttpContext context)
    {
        ProblemDetails problem = GlobalExceptionHandler.Problem(
            StatusCodes.Status503ServiceUnavailable, ErrorCode.MaintenanceMode, MaintenanceModeGuard.BlockedMessage);
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
