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
    private readonly string? supportContact;

    public SmtpHandoverInvitationSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;

        string? clientUrl = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(clientUrl))
        {
            clientUrl = DefaultClientUrl;
        }
        receiveUrl = clientUrl.TrimEnd('/') + "/receive";

        // Liên hệ hỗ trợ của nhóm lấy từ cấu hình (App:SupportEmail, App:SupportPhone); không có thì thư chỉ bảo báo lại cho LegacyVault.
        string? supportEmail = configuration["App:SupportEmail"];
        string? supportPhone = configuration["App:SupportPhone"];
        bool hasEmail = !string.IsNullOrWhiteSpace(supportEmail);
        bool hasPhone = !string.IsNullOrWhiteSpace(supportPhone);
        if (hasEmail && hasPhone)
        {
            supportContact = $"email {supportEmail!.Trim()} hoặc số {supportPhone!.Trim()}";
        }
        else if (hasEmail)
        {
            supportContact = $"email {supportEmail!.Trim()}";
        }
        else if (hasPhone)
        {
            supportContact = $"số {supportPhone!.Trim()}";
        }
    }

    public Task SendAsync(HandoverInvitation invitation, CancellationToken ct)
    {
        string link = receiveUrl + "?token=" + Uri.EscapeDataString(invitation.Token);
        EmailContent content = HandoverInvitationEmail.Build(invitation, link, supportContact);
        return mailSender.SendAsync(invitation.ToEmail, invitation.FullName, content, ct);
    }
}
