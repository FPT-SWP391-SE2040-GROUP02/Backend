using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi thư báo biên bản qua SMTP (SmtpMailSender). Gửi lỗi thì ném exception; notifier nuốt lỗi này.</summary>
public sealed class SmtpHandoverDocumentNoticeSender : IHandoverDocumentNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string appUrl;

    public SmtpHandoverDocumentNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        appUrl = clientUrl.TrimEnd('/') + "/";
    }

    public Task SendAsync(HandoverDocumentNotice notice, CancellationToken ct)
    {
        EmailContent content = HandoverDocumentNoticeEmail.Build(notice, appUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content, ct);
    }
}
