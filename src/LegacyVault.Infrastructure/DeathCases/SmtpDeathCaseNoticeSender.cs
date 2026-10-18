using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Gửi email báo Owner thật qua Gmail (SmtpMailSender). SMTP nhận thư = đã giao (D3);
/// gửi lỗi thì ném exception để DeathCaseNoticeJob thử lại.
/// </summary>
public sealed class SmtpDeathCaseNoticeSender : IDeathCaseNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string loginUrl;

    public SmtpDeathCaseNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        loginUrl = clientUrl.TrimEnd('/') + "/login";
    }

    public Task SendAsync(DeathCaseOwnerNotice notice, CancellationToken ct)
    {
        EmailContent content = DeathCaseNoticeEmail.Build(
            notice.FullName, notice.CaseCode, notice.ExpectedObjectionEndsAt, loginUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}
