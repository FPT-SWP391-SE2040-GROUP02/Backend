using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace LegacyVault.Api.Config;

/// <summary>
/// Endpoint thử gửi email dành riêng cho môi trường Development (R6, audit 2026-10-27).
/// Ở môi trường thật route không được đăng ký (trả về 404), tránh bị lợi dụng làm máy gửi thư.
/// </summary>
public static class DevEmailEndpoints
{
    public static void MapDevEmailEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapPost("/api/v1/auth/test-email", SendTestEmailAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> SendTestEmailAsync(
        string to,
        SmtpMailSender mailSender,
        IOptions<SmtpSettings> smtpOpts,
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken ct)
    {
        User? caller = null;
        if (currentUser.UserId != null)
        {
            caller = await users.FindByIdAsync(currentUser.UserId.Value, ct);
        }

        if (caller == null || caller.Role != UserRole.Admin || caller.Status != UserStatus.Active)
        {
            return Results.NotFound();
        }

        try
        {
            SmtpSettings settings = smtpOpts.Value;
            if (!settings.IsConfigured)
            {
                return Results.Json(new
                {
                    success = false,
                    error = "Email chưa được cấu hình. Cần Email__WebhookUrl hoặc (Email__Smtp__UserName và Email__Smtp__Password).",
                    provider = "None",
                    userNameSet = !string.IsNullOrWhiteSpace(settings.ResolveUserName()),
                    passwordSet = !string.IsNullOrWhiteSpace(settings.ResolvePassword())
                }, statusCode: 400);
            }

            string provider = !string.IsNullOrWhiteSpace(settings.ResolveWebhookUrl())
                ? "Webhook (Google Apps Script HTTPS)"
                : $"SMTP ({settings.ResolveHost()}:{settings.Port})";

            await mailSender.SendAsync(
                to,
                "Người dùng thử nghiệm",
                "Thử nghiệm gửi email từ LegacyVault",
                "Chào bạn, đây là email kiểm tra kết nối email từ LegacyVault.",
                "<div style='font-family: sans-serif; padding: 20px; background: #f4f6f8; border-radius: 8px;'><h2 style='color: #1a56db;'>LegacyVault Email Test</h2><p>Xin chúc mừng! Hệ thống gửi email tự động từ <b>" + settings.ResolveFromAddress() + "</b> qua <b>" + provider + "</b> đã hoạt động thành công 100%.</p></div>",
                ct);

            return Results.Ok(new
            {
                success = true,
                provider = provider,
                message = $"Đã gửi email thử nghiệm tới {to} từ {settings.ResolveFromAddress()} qua {provider}"
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                errorType = ex.GetType().Name
            }, statusCode: 500);
        }
    }
}
