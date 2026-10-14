using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // TimeProvider đăng ký một lần ở InfrastructureConfig.
        services.AddScoped<IVaultService, VaultService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IContainerService, ContainerService>();
        services.AddScoped<IBeneficiaryService, BeneficiaryService>();
        services.AddHandoverApplication();

        // Điểm danh (deadman switch)
        services.AddScoped<ICheckInService, CheckInService>();
        services.AddScoped<ICheckInScheduler, CheckInScheduler>();
        services.AddScoped<ICheckInJob, CheckInJob>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();

        // Đăng nhập Google
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddScoped<LoginSessionIssuer>();
        services.AddScoped<LoginThrottleGuard>();
        return services;
    }
}
