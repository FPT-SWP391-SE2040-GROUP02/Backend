using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Đăng ký service nghiệp vụ tại đây khi bắt đầu feature.
        return services;
    }
}
