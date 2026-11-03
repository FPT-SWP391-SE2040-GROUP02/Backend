using LegacyVault.Application.ServiceConfig;
using LegacyVault.Infrastructure.Billing;
using LegacyVault.Infrastructure.Ekyc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>
/// Giá trị mặc định của dịch vụ ngoài đọc từ cấu hình máy chủ (appsettings, user-secrets, biến môi trường).
/// Gemini: Gemini:ApiKey, Gemini:Model (tùy chọn), Gemini:TimeoutSeconds (tùy chọn).
/// </summary>
public sealed class SystemConfigDefaults : ISystemConfigDefaults
{
    private readonly IConfiguration configuration;
    private readonly IOptions<EkycSettings> ekycOptions;
    private readonly IOptions<BillingSettings> billingOptions;

    public SystemConfigDefaults(
        IConfiguration configuration, IOptions<EkycSettings> ekycOptions, IOptions<BillingSettings> billingOptions)
    {
        this.configuration = configuration;
        this.ekycOptions = ekycOptions;
        this.billingOptions = billingOptions;
    }

    public ResolvedConfig? ReadPrimary(string serviceKey)
    {
        if (serviceKey == SystemConfigServices.Gemini)
        {
            return ReadGemini();
        }
        if (serviceKey == SystemConfigServices.Ekyc)
        {
            return ReadEkyc();
        }
        if (serviceKey == SystemConfigServices.SePay)
        {
            return ReadSePay();
        }
        return null;
    }

    /// <summary>SePay và tài khoản nhận tiền theo mục "Billing:SePay" và "Billing:Bank" (không đụng tới bảng giá "Billing:Pricing").</summary>
    private ResolvedConfig ReadSePay()
    {
        BillingSettings settings = billingOptions.Value;
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.AppSettings;
        config.Settings = SystemConfigCatalog.DefaultSettings(SystemConfigServices.SePay);
        string environment = (settings.SePay.Environment ?? string.Empty).Trim();
        if (environment.Length > 0)
        {
            config.Settings["environment"] = environment;
        }
        config.Settings["bankCode"] = (settings.Bank.BankCode ?? string.Empty).Trim();
        config.Settings["accountNumber"] = (settings.Bank.AccountNumber ?? string.Empty).Trim();
        config.Settings["accountName"] = (settings.Bank.AccountName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(settings.SePay.WebhookApiKey))
        {
            config.Secrets["webhookApiKey"] = settings.SePay.WebhookApiKey.Trim();
        }
        return config;
    }

    /// <summary>eKYC theo mục "Ekyc" đã bind và làm sạch lúc khởi động (cắt khoảng trắng và dấu nháy, mặc định thời gian chờ).</summary>
    private ResolvedConfig ReadEkyc()
    {
        EkycSettings settings = ekycOptions.Value;
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.AppSettings;
        config.Settings = SystemConfigCatalog.DefaultSettings(SystemConfigServices.Ekyc);
        config.Settings["baseUrl"] = (settings.BaseUrl ?? string.Empty).Trim();
        if (settings.TimeoutSeconds > 0)
        {
            config.Settings["timeoutSeconds"] = settings.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            config.Secrets["apiKey"] = settings.ApiKey.Trim();
        }
        return config;
    }

    private ResolvedConfig ReadGemini()
    {
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.AppSettings;
        config.Settings = SystemConfigCatalog.DefaultSettings(SystemConfigServices.Gemini);
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
