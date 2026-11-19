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
    Path.Combine(Directory.GetCurrentDirectory(), "Backend", ".env"),
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
// (HSTS, redirect HTTPS và rate limit theo IP người dùng). Tin proxy nào: xem ForwardedHeadersConfig.
app.UseForwardedHeaders(ForwardedHeadersConfig.Create(builder.Configuration));

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
        int hashed = await LegacyVault.Infrastructure.Security.CitizenIdHashBackfill.RunAsync(
            db, scope.ServiceProvider.GetRequiredService<LegacyVault.Application.Security.IOwnerIdentityProtector>(),
            scope.ServiceProvider.GetRequiredService<LegacyVault.Application.Security.ICitizenIdBlindIndex>(), app.Logger, CancellationToken.None);
        if (hashed > 0)
        {
            Console.WriteLine($"--> [Backfill CCCD] Đã điền hash cho {hashed} danh tính.");
        }
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
// Bảo trì: cần biết vai trò từ Bearer token nên đặt sau UseAuthentication, trước rate limit và controller.
app.UseMiddleware<LegacyVault.Api.Security.MaintenanceModeMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapBillingDevEndpoints();

// R6 (audit 2026-10-27): endpoint thử email CHỈ đăng ký ở Development (pattern như BillingDevEndpoints).
// Ở môi trường thật route là 404, không thể bị người ngoài dùng server làm máy gửi thư.
if (app.Environment.IsDevelopment())
{
    app.MapDevEmailEndpoints();
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
