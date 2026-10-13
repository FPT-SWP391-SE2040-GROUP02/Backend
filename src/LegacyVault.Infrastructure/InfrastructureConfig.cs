using LegacyVault.Application.Repositories;
using LegacyVault.Application.Storage;
using LegacyVault.Infrastructure.Data;
using LegacyVault.Infrastructure.Health;
using LegacyVault.Infrastructure.Repositories;
using LegacyVault.Infrastructure.Storage;
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
                "Thiếu ConnectionStrings:Default. Khi chạy local, chọn profile http (Development).")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.Default));
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // IUnitOfWork chính là AppDbContext của request: repository và service dùng chung một instance.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IVaultRepository, VaultRepository>();
        services.AddScoped<IContainerRepository, ContainerRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();

        services.AddOptions<AssetStorageSettings>()
            .Bind(configuration.GetSection("AssetStorage"))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.RootPath),
                "Thiếu AssetStorage:RootPath.")
            .ValidateOnStart();

        // KEK cho mã hóa nội dung tài sản. Sai/thiếu khóa thì API không khởi động, tránh lưu nội dung không mở lại được.
        services.AddOptions<AssetEncryptionSettings>()
            .Bind(configuration.GetSection("AssetEncryption"))
            .Validate(settings => DataKeyProtector.FindConfigurationError(settings) == null,
                "Cấu hình AssetEncryption không hợp lệ (cần ActiveKeyId và Keys là base64 32 byte). "
                + "Khi chạy local, chọn profile http (Development).")
            .ValidateOnStart();
        services.AddSingleton<DataKeyProtector>();
        services.AddSingleton<IAssetContentStore, LocalAssetContentStore>();
        return services;
    }
}
