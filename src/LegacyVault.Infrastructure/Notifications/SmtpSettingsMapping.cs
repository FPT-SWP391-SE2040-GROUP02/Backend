using System.Globalization;
using LegacyVault.Application.ServiceConfig;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Đổi cấu hình dịch vụ SMTP đang có hiệu lực (trường thường và mật khẩu) thành SmtpSettings để dùng lại logic gửi sẵn có.</summary>
public static class SmtpSettingsMapping
{
    public static SmtpSettings FromConfig(ResolvedConfig config)
    {
        SmtpSettings settings = new SmtpSettings();
        settings.Host = Text(config, "host");
        settings.SecureSocket = Text(config, "secureSocket");
        settings.UserName = Text(config, "userName");
        settings.FromAddress = Text(config, "fromAddress");
        settings.FromName = Text(config, "fromName");
        settings.WebhookUrl = Text(config, "webhookUrl");
        int port = 0;
        if (int.TryParse(Text(config, "port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0)
        {
            settings.Port = port;
        }
        int timeout = 0;
        if (int.TryParse(Text(config, "timeoutSeconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout)
            && timeout > 0)
        {
            settings.TimeoutSeconds = timeout;
        }
        string? password = null;
        if (config.Secrets.TryGetValue("password", out password))
        {
            settings.Password = password;
        }
        return settings;
    }

    private static string Text(ResolvedConfig config, string name)
    {
        string? value = null;
        if (config.Settings.TryGetValue(name, out value))
        {
            return value;
        }
        return string.Empty;
    }
}
