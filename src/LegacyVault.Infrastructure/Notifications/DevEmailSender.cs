using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Bản dùng khi chưa cấu hình Gmail (Email:Smtp): không gửi thư thật mà in link ra cửa sổ chạy API
/// để người phát triển chép link thử. Có cấu hình thì EmailConfig dùng SmtpEmailSender thay thế.
/// </summary>
public sealed class DevEmailSender : IEmailSender
{
    private readonly ILogger<DevEmailSender> _logger;

    public DevEmailSender(ILogger<DevEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailVerificationAsync(string toEmail, string fullName, string verificationUrl, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "======================================================================\n" +
                "[EMAIL XAC MINH] Gui toi: {Email} ({FullName})\n" +
                "Link xac minh: {VerificationUrl}\n" +
                "======================================================================",
                toEmail, fullName, verificationUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong the ghi log/gui email xac minh toi {Email}", toEmail);
        }

        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string toEmail, string fullName, string resetUrl, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "======================================================================\n" +
                "[EMAIL DAT LAI MAT KHAU] Gui toi: {Email} ({FullName})\n" +
                "Link dat lai: {ResetUrl}\n" +
                "======================================================================",
                toEmail, fullName, resetUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Khong the ghi log/gui email dat lai mat khau toi {Email}", toEmail);
        }

        return Task.CompletedTask;
    }
}
