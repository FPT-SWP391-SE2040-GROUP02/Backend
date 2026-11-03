using LegacyVault.Application.ServiceConfig;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Giá trị mặc định của dịch vụ ngoài đọc từ cấu hình máy chủ (appsettings, user-secrets, biến môi trường).
/// Gemini: Gemini:ApiKey, Gemini:Model (tùy chọn), Gemini:TimeoutSeconds (tùy chọn).
/// </summary>
public sealed class SystemConfigDefaults : ISystemConfigDefaults
{
    private readonly IConfiguration configuration;

    public SystemConfigDefaults(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public ResolvedConfig? ReadPrimary(string serviceKey)
    {
        if (serviceKey != SystemConfigServices.Gemini)
        {
            return null;
        }

        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.AppSettings;
        config.Settings = SystemConfigCatalog.DefaultSettings(serviceKey);
        string? model = configuration["Gemini:Model"];
        if (!string.IsNullOrWhiteSpace(model))
        {
            config.Settings["model"] = model.Trim();
        }
        string? timeout = configuration["Gemini:TimeoutSeconds"];
        if (!string.IsNullOrWhiteSpace(timeout))
        {
            config.Settings["timeoutSeconds"] = timeout.Trim();
        }
        string? apiKey = configuration["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            config.Secrets["apiKey"] = apiKey.Trim();
        }
        return config;
    }
}
