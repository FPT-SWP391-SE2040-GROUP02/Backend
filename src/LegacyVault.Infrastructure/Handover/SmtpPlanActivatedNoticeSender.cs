using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi thư báo người nhận khi kích hoạt kế hoạch qua Gmail (SmtpMailSender). Gửi lỗi thì ném exception.</summary>
public sealed class SmtpPlanActivatedNoticeSender : IPlanActivatedNoticeSender
{
    private readonly SmtpMailSender mailSender;
    private readonly string landingUrl;

    public SmtpPlanActivatedNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string clientUrl = ClientUrlSettings.Read(configuration);
        landingUrl = clientUrl.TrimEnd('/') + "/";
    }

    public Task SendAsync(PlanActivatedNotice notice, CancellationToken ct)
    {
        string actionUrl = landingUrl;
        if (notice.RequestToken.Length > 0)
        {
            actionUrl = landingUrl + "request?token=" + Uri.EscapeDataString(notice.RequestToken);
        }
        EmailContent content = PlanActivatedNoticeEmail.Build(notice, actionUrl);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, content, ct);
    }
}
