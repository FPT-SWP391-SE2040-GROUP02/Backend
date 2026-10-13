using LegacyVault.Api.Security;
using LegacyVault.Application.Security;

namespace LegacyVault.Api.Config;

public static class AuthConfig
{
    /// <summary>
    /// Chưa có đăng nhập thật: dùng DevHeader (chỉ Development, cần DevAuth:Enabled = true).
    /// Ngoài Development mọi endpoint [Authorize] trả 401 cho tới khi có JWT.
    /// </summary>
    public static IServiceCollection AddAuthConfig(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var devAuthEnabled = configuration.GetValue<bool>("DevAuth:Enabled");
        if (devAuthEnabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("DevAuth:Enabled chỉ được bật ở môi trường Development.");
        }

        services.AddAuthentication(DevHeaderAuthenticationHandler.SchemeName)
            .AddScheme<DevHeaderAuthenticationOptions, DevHeaderAuthenticationHandler>(
                DevHeaderAuthenticationHandler.SchemeName, options => options.Enabled = devAuthEnabled);
        services.AddAuthorization();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}
