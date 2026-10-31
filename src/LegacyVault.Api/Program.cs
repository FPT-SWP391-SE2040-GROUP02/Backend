using LegacyVault.Api.Config;
using LegacyVault.Application;
using LegacyVault.Infrastructure;
using LegacyVault.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Tự động nạp cấu hình từ .env nếu có (dành cho chạy local và môi trường Docker/Render)
var envCandidates = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env"),
    Directory.GetParent(Directory.GetCurrentDirectory()) != null ? Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory())!.FullName, ".env") : ""
};
foreach (var envPath in envCandidates)
{
    if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
    {
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var separatorIdx = trimmed.IndexOf('=');
            if (separatorIdx <= 0) continue;
            var key = trimmed[..separatorIdx].Trim();
            var val = trimmed[(separatorIdx + 1)..].Trim().Trim('"').Trim('\'');
            builder.Configuration[key.Replace("__", ":")] = val;
            builder.Configuration[key] = val;
        }
        break;
    }
}

builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);

// Render injects PORT; ASP.NET Core mặc định không đọc biến này nên phải tự gắn URL.
if (int.TryParse(builder.Configuration["PORT"], out int renderPort) && renderPort > 0)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiConfig(builder.Configuration);
builder.Services.AddCorsConfig(builder.Configuration);

var app = builder.Build();

// Render/Vercel đặt API sau reverse proxy: tin X-Forwarded-* để scheme/IP client đúng
// (HSTS, redirect HTTPS và rate limit theo IP người dùng).
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// Migrate tùy chọn khi deploy: bật bằng Database__MigrateOnStartup=true.
// Tắt mặc định để local/CI không đổi schema ngoài dự kiến.
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    try
    {
        Console.WriteLine("--> [Database Migration] Đang bắt đầu cập nhật schema cơ sở dữ liệu...");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Console.WriteLine("--> [Database Migration] Cập nhật schema cơ sở dữ liệu thành công!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> [Database Migration LỖI]: {ex.Message}");
        app.Logger.LogError(ex, "Lỗi khi chạy Migration tự động lúc khởi động.");
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "LegacyVault API v1"));
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseCors(CorsConfig.FrontendPolicy);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapBillingDevEndpoints();

// R6 (audit 2026-10-27): endpoint thử email CHỈ đăng ký ở Development (pattern như BillingDevEndpoints).
// Ở môi trường thật route là 404, không thể bị người ngoài dùng server làm máy gửi thư.
if (app.Environment.IsDevelopment())
{
app.MapPost("/api/v1/auth/test-email", async (string to, LegacyVault.Infrastructure.Notifications.SmtpMailSender mailSender, Microsoft.Extensions.Options.IOptions<LegacyVault.Infrastructure.Notifications.SmtpSettings> smtpOpts, LegacyVault.Application.Security.ICurrentUser currentUser, LegacyVault.Application.Repositories.IUserRepository users, CancellationToken ct) =>
{
    LegacyVault.Domain.Entities.User? caller = null;
    if (currentUser.UserId != null)
    {
        caller = await users.FindByIdAsync(currentUser.UserId.Value, ct);
    }
    if (caller == null || caller.Role != LegacyVault.Domain.Enums.UserRole.Admin || caller.Status != LegacyVault.Domain.Enums.UserStatus.Active)
    {
        return Results.NotFound();
    }

    try
    {
        var settings = smtpOpts.Value;
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

        string provider = !string.IsNullOrWhiteSpace(settings.ResolveWebhookUrl()) ? "Webhook (Google Apps Script HTTPS)" :                          $"SMTP ({settings.ResolveHost()}:{settings.Port})";

        await mailSender.SendAsync(to, "Người dùng thử nghiệm", "Thử nghiệm gửi email từ LegacyVault",
            "Chào bạn, đây là email kiểm tra kết nối email từ LegacyVault.",
            "<div style='font-family: sans-serif; padding: 20px; background: #f4f6f8; border-radius: 8px;'><h2 style='color: #1a56db;'>LegacyVault Email Test</h2><p>Xin chúc mừng! Hệ thống gửi email tự động từ <b>" + settings.ResolveFromAddress() + "</b> qua <b>" + provider + "</b> đã hoạt động thành công 100%.</p></div>", ct);
        return Results.Ok(new { success = true, provider, message = $"Đã gửi email thử nghiệm tới {to} từ {settings.ResolveFromAddress()} qua {provider}" });
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
}).RequireAuthorization();
}

// Liveness không phụ thuộc SQL; readiness chỉ đạt khi kết nối được database.
app.MapGet("/", () => Results.Ok(new { status = "Healthy", service = "LegacyVault API", version = "v1" }));
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Không tự migrate/seed lúc khởi động. Docker Compose tạo database dev trống.
app.Run();

public partial class Program;
