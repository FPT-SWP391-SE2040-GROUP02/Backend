using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi thư mốc lưu dữ liệu thật qua SMTP.</summary>
public sealed class SmtpRetentionNoticeSender : IRetentionNoticeSender
{
    private readonly SmtpMailSender mailSender;
    private readonly string loginUrl;

    public SmtpRetentionNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string clientUrl = ClientUrlSettings.Read(configuration);
        loginUrl = clientUrl.TrimEnd('/') + "/login";
    }

    public Task SendAsync(RetentionNotice notice, CancellationToken ct)
    {
        EmailContent content = RetentionNoticeEmail.Build(notice, loginUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}
