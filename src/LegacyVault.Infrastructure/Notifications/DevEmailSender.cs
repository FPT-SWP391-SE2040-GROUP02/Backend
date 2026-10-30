using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

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
        // Reset URL grants access to the account; never write it or recipient PII to logs.
        _logger.LogInformation("Password reset email not delivered: SMTP is not configured.");
        return Task.CompletedTask;
    }

    public Task SendVaultPinNoticeAsync(
        string toEmail, string fullName, VaultPinNoticeKind kind, DateTimeOffset? lockedUntil, CancellationToken ct)
    {
        // Không ghi email người nhận hay PIN vào log.
        _logger.LogInformation("Vault PIN notice ({Kind}) not delivered: SMTP is not configured.", kind);
        return Task.CompletedTask;
    }
}
