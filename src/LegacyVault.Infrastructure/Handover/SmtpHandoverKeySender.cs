using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi thư khóa mở gói thật qua SmtpMailSender. Thư không có tên gói hay tài sản (OPS-04).</summary>
public sealed class SmtpHandoverKeySender : IHandoverKeySender
{
    private readonly SmtpMailSender mailSender;

    public SmtpHandoverKeySender(SmtpMailSender mailSender)
    {
        this.mailSender = mailSender;
    }

    public Task SendAsync(HandoverKeyMail mail, CancellationToken ct)
    {
        string subject = "Khóa mở gói di sản số trên LegacyVault";
        string lead = "Người thực hiện gửi bạn khóa mở gói: " + mail.Key
            + ". Hãy nhập khóa này ở bước \"Nhập khóa mở gói\" khi nhận tài sản. Không chia sẻ khóa cho ai khác.";
        const string note = "Thư này chỉ có khóa, chưa đủ để nhận tài sản: bạn vẫn cần liên kết mời và tài khoản đã xác minh. "
            + "Nếu bạn không biết về việc này, hãy bỏ qua email.";
        string text = $"Xin chào {mail.FullName},\n\n{lead}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Khóa mở gói di sản số", mail.FullName, lead, string.Empty, string.Empty, note, "Khóa mở gói", true);
        return mailSender.SendAsync(mail.ToEmail, mail.FullName, subject, text, html, ct);
    }
}
