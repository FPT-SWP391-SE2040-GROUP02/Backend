using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi thư báo người nhận khi kích hoạt kế hoạch qua Gmail (SmtpMailSender). Gửi lỗi thì ném exception.</summary>
public sealed class SmtpPlanActivatedNoticeSender : IPlanActivatedNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string landingUrl;

    public SmtpPlanActivatedNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        landingUrl = clientUrl.TrimEnd('/') + "/";
    }

    public Task SendAsync(PlanActivatedNotice notice, CancellationToken ct)
    {
        EmailContent content = PlanActivatedNoticeEmail.Build(notice, landingUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}
