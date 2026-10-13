using LegacyVault.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IVaultService, VaultService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IContainerService, ContainerService>();
        return services;
    }
}
