namespace LegacyVault.Api.Config;

public static class CorsConfig
{
    public const string FrontendPolicy = "Frontend";

    public static IServiceCollection AddCorsConfig(
        this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendPolicy, policy =>
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
        // Chưa có auth. Bật credentials cùng contract cookie/CSRF khi triển khai auth.
        return services;
    }
}
