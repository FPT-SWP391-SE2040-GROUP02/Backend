using LegacyVault.Application.ServiceConfig;
using LegacyVault.Infrastructure.Billing;
using LegacyVault.Infrastructure.Ekyc;
using LegacyVault.Infrastructure.Notifications;
using LegacyVault.Infrastructure.Storage;
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
    private readonly IOptions<SmtpSettings> smtpOptions;
    private readonly IOptions<AssetStorageSettings> storageOptions;

    public SystemConfigDefaults(
        IConfiguration configuration,
        IOptions<EkycSettings> ekycOptions,
        IOptions<BillingSettings> billingOptions,
        IOptions<SmtpSettings> smtpOptions,
        IOptions<AssetStorageSettings> storageOptions)
    {
        this.smtpOptions = smtpOptions;
        this.storageOptions = storageOptions;
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
        if (serviceKey == SystemConfigServices.Smtp)
        {
            return ReadSmtp();
        }
        if (serviceKey == SystemConfigServices.R2)
        {
            return ReadR2();
        }
        return null;
    }

    /// <summary>R2 theo mục "AssetStorage:R2" (tài khoản, bucket; khóa truy cập chỉ đặt bằng user-secrets hoặc biến môi trường).</summary>
    private ResolvedConfig ReadR2()
    {
        R2StorageSettings settings = storageOptions.Value.R2;
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.AppSettings;
        config.Settings = SystemConfigCatalog.DefaultSettings(SystemConfigServices.R2);
        config.Settings["accountId"] = (settings.AccountId ?? string.Empty).Trim();
        config.Settings["bucketName"] = (settings.BucketName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(settings.AccessKeyId))
        {
            config.Secrets["accessKeyId"] = settings.AccessKeyId.Trim();
        }
        if (!string.IsNullOrWhiteSpace(settings.SecretAccessKey))
        {
            config.Secrets["secretAccessKey"] = settings.SecretAccessKey.Trim();
        }
        return config;
    }

    /// <summary>
    /// Email theo SmtpSettings đã bind và làm sạch lúc khởi động (kể cả các tên cấu hình cũ như SMTP__HOST), nên mặc định
    /// đúng bằng cách hệ thống đang gửi thư hôm nay.
    /// </summary>
    private ResolvedConfig ReadSmtp()
    {
        SmtpSettings settings = smtpOptions.Value;
        ResolvedConfig config = new ResolvedConfig();
        config.Source = ConfigSources.AppSettings;
        config.Settings = SystemConfigCatalog.DefaultSettings(SystemConfigServices.Smtp);
        config.Settings["host"] = (settings.Host ?? string.Empty).Trim();
        config.Settings["port"] = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        config.Settings["secureSocket"] = (settings.SecureSocket ?? string.Empty).Trim();
        config.Settings["userName"] = (settings.UserName ?? string.Empty).Trim();
        config.Settings["fromAddress"] = (settings.FromAddress ?? string.Empty).Trim();
        config.Settings["fromName"] = (settings.FromName ?? string.Empty).Trim();
        config.Settings["timeoutSeconds"] = settings.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        config.Settings["webhookUrl"] = (settings.WebhookUrl ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(settings.Password))
        {
            config.Secrets["password"] = settings.Password.Trim();
        }
        return config;
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
