using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Đăng ký dịch vụ email. Có đủ Email:Smtp:UserName và Password thì gửi email thật qua Gmail;
/// chưa cấu hình thì dùng bản chỉ ghi log để dev/test vẫn chạy được. Chọn lúc cần dùng, nên đăng ký này ghi đè
/// các đăng ký IEmailSender/ICheckInReminderSender khác đặt trước nó.
/// </summary>
public static class EmailConfig
{
    public static IServiceCollection AddEmailServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpSettings>()
            .Bind(configuration.GetSection(SmtpSettings.SectionName))
            .PostConfigure(settings =>
            {
                // Tự động nhận diện cấu hình định dạng SMTP__... hoặc SMTP:...
                if (string.IsNullOrWhiteSpace(settings.Host) || settings.Host == "smtp.gmail.com")
                {
                    string? altHost = configuration["SMTP:HOST"] ?? configuration["SMTP__HOST"] ?? configuration["Smtp:Host"];
                    if (!string.IsNullOrWhiteSpace(altHost)) settings.Host = altHost;
                }
                if (string.IsNullOrWhiteSpace(settings.UserName))
                {
                    string? altUser = configuration["SMTP:USERNAME"] ?? configuration["SMTP__USERNAME"] ?? configuration["Smtp:UserName"];
                    if (!string.IsNullOrWhiteSpace(altUser)) settings.UserName = altUser;
                }
                if (string.IsNullOrWhiteSpace(settings.Password))
                {
                    string? altPass = configuration["SMTP:PASSWORD"] ?? configuration["SMTP__PASSWORD"] ?? configuration["Smtp:Password"];
                    if (!string.IsNullOrWhiteSpace(altPass)) settings.Password = altPass;
                }
                if (string.IsNullOrWhiteSpace(settings.FromAddress))
                {
                    string? altSender = configuration["SMTP:SENDEREMAIL"] ?? configuration["SMTP__SENDEREMAIL"] ?? configuration["Smtp:SenderEmail"] ?? configuration["SMTP:FROMADDRESS"] ?? configuration["SMTP__FROMADDRESS"];
                    if (!string.IsNullOrWhiteSpace(altSender)) settings.FromAddress = altSender;
                }
                string? altSenderName = configuration["SMTP:SENDERNAME"] ?? configuration["SMTP__SENDERNAME"] ?? configuration["Smtp:SenderName"];
                if (!string.IsNullOrWhiteSpace(altSenderName) && (string.IsNullOrWhiteSpace(settings.FromName) || settings.FromName == "LegacyVault"))
                {
                    settings.FromName = altSenderName;
                }
                string? rawPort = configuration["Email:Smtp:Port"] ?? configuration["Email__Smtp__Port"] ?? configuration["SMTP:PORT"] ?? configuration["SMTP__PORT"] ?? configuration["Smtp:Port"];
                if (!string.IsNullOrWhiteSpace(rawPort))
                {
                    string cleanedPort = rawPort.Trim().Trim('"').Trim('\'');
                    if (int.TryParse(cleanedPort, out int port) && port > 0)
                    {
                        settings.Port = port;
                    }
                }

                // Cắt bỏ dấu nháy kép / nháy đơn thừa nếu dán từ môi trường
                settings.Host = settings.Host?.Trim().Trim('"').Trim('\'') ?? "smtp.gmail.com";
                settings.UserName = settings.UserName?.Trim().Trim('"').Trim('\'') ?? string.Empty;
                settings.Password = settings.Password?.Trim().Trim('"').Trim('\'') ?? string.Empty;
                settings.FromAddress = settings.FromAddress?.Trim().Trim('"').Trim('\'') ?? string.Empty;
                settings.FromName = settings.FromName?.Trim().Trim('"').Trim('\'') ?? "LegacyVault";
                settings.SecureSocket = settings.SecureSocket?.Trim().Trim('"').Trim('\'') ?? "StartTls";
            })
            .Validate(settings => settings.FindError() == null,
                "Cấu hình Email:Smtp không hợp lệ (Port 1–65535, SecureSocket đúng tên, FromAddress là email).")
            .ValidateOnStart();
        services.AddSingleton<SmtpMailSender>();
        services.AddHostedService<EmailStartupLogger>();
        services.AddSingleton<ICheckInReminderSender>(provider => ChooseReminderSender(provider));
        services.AddSingleton<IEmailSender>(provider => ChooseVerificationSender(provider));
        return services;
    }

    private static ICheckInReminderSender ChooseReminderSender(IServiceProvider provider)
    {
        if (IsConfigured(provider))
        {
            return ActivatorUtilities.CreateInstance<SmtpCheckInReminderSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<LogCheckInReminderSender>(provider);
    }

    private static IEmailSender ChooseVerificationSender(IServiceProvider provider)
    {
        if (IsConfigured(provider))
        {
            return ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider);
        }
        return ActivatorUtilities.CreateInstance<DevEmailSender>(provider);
    }

    private static bool IsConfigured(IServiceProvider provider)
    {
        return provider.GetRequiredService<IOptions<SmtpSettings>>().Value.IsConfigured;
    }
}
