namespace LegacyVault.Api.Config;

public static class CorsConfig
{
    public const string FrontendPolicy = "Frontend";

    public static IServiceCollection AddCorsConfig(
        this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendPolicy, policy =>
            policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin))
                    {
                        return false;
                    }
                    Uri uri;
                    try
                    {
                        uri = new Uri(origin);
                    }
                    catch (UriFormatException)
                    {
                        return false;
                    }
                    return uri.Host == "localhost"
                        || uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase)
                        || origins.Contains(origin);
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
        // Credentials: chỉ cho origin "đúng" (localhost, *.vercel.app, hoặc danh sách cấu hình);
        // cookie POST vẫn được bảo vệ bằng antiforgery.
        return services;
    }
}
