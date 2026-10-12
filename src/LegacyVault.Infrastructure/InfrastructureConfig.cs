using LegacyVault.Infrastructure.Data;
using LegacyVault.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure;

public static class InfrastructureConfig
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseSettings>()
            .Bind(configuration.GetSection("ConnectionStrings"))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Default),
                "Thiếu ConnectionStrings:Default. Chạy scripts/setup-local.ps1 trước khi chạy API.")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.Default));
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Đăng ký repository và tích hợp bên ngoài tại đây khi có feature.
        return services;
    }
}
