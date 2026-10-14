using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application.Services;

/// <summary>Đăng ký service của giai đoạn nhận tài sản. ApplicationConfig chỉ gọi một dòng vào đây.</summary>
public static class HandoverApplicationConfig
{
    public static IServiceCollection AddHandoverApplication(this IServiceCollection services)
    {
        services.AddScoped<IHandoverStarter, HandoverStarter>();
        services.AddScoped<IHandoverService, HandoverService>();
        services.AddScoped<IHandoverDownloadService, HandoverDownloadService>();
        services.AddScoped<IHandoverJob, HandoverJob>();
        return services;
    }
}
