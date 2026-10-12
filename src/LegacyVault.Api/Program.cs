using LegacyVault.Api.Config;
using LegacyVault.Application;
using LegacyVault.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiConfig();
builder.Services.AddCorsConfig(builder.Configuration);

var app = builder.Build();

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

app.UseCors(CorsConfig.FrontendPolicy);
app.MapControllers();

// Liveness không phụ thuộc SQL; readiness chỉ đạt khi kết nối được database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Không tự migrate/seed lúc khởi động. Script setup tạo database dev trống.
app.Run();

public partial class Program;
