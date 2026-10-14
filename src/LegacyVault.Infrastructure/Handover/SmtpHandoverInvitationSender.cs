using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi email mời thật qua Gmail (SmtpMailSender). Gửi lỗi thì ném exception để HandoverStarter ghi nhận.</summary>
public sealed class SmtpHandoverInvitationSender : IHandoverInvitationSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string receiveUrl;

    public SmtpHandoverInvitationSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        receiveUrl = clientUrl.TrimEnd('/') + "/receive";
    }

    public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
    {
        string link = receiveUrl + "?token=" + Uri.EscapeDataString(invitation.Token);
        EmailContent content = HandoverInvitationEmail.Build(invitation.FullName, link, invitation.ResponseDeadline);
        return mailSender.SendAsync(invitation.ToEmail, invitation.FullName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }
}
