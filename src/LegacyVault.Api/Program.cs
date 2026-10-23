using LegacyVault.Api.Config;
using LegacyVault.Application;
using LegacyVault.Infrastructure;
using LegacyVault.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

// Liveness không phụ thuộc SQL; readiness chỉ đạt khi kết nối được database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Không tự migrate/seed lúc khởi động. Docker Compose tạo database dev trống.
app.Run();

public partial class Program;
