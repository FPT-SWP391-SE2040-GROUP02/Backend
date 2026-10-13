using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();

        // Đăng nhập Google
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<LoginSessionIssuer>();
        services.AddScoped<LoginThrottleGuard>();
        return services;
    }
}
