using LegacyVault.Application.Notifications;
using LegacyVault.Infrastructure.Notifications;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Gửi thư mã nhận thật qua SmtpMailSender. Thư không có tên gói hay tài sản.</summary>
public sealed class SmtpHandoverKeySender : IHandoverKeySender
{
    private readonly SmtpMailSender mailSender;

    public SmtpHandoverKeySender(SmtpMailSender mailSender)
    {
        this.mailSender = mailSender;
    }

    public Task SendAsync(HandoverKeyMail mail, CancellationToken ct)
    {
        string subject = "Mã nhận di sản số trên LegacyVault";
        const string lead = "Người thực hiện gửi bạn mã nhận ở khung bên dưới. "
            + "Hãy nhập mã này ở bước \"Nhập mã nhận\" khi nhận tài sản. Không chia sẻ mã cho ai khác.";
        const string note = "Thư này chỉ có mã nhận, chưa đủ để nhận tài sản: bạn vẫn cần liên kết mời và tài khoản đã xác minh. "
            + "Nếu bạn không biết về việc này, hãy bỏ qua email.";
        string text = $"Xin chào {mail.FullName},\n\nNgười thực hiện gửi bạn mã nhận: {mail.Key}\n\n"
            + "Hãy nhập mã này ở bước \"Nhập mã nhận\" khi nhận tài sản. Không chia sẻ mã cho ai khác.\n\n"
            + note + "\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Mã nhận di sản số", mail.FullName, lead, string.Empty, string.Empty, note, "Mã nhận", true,
            highlightCode: mail.Key, highlightLabel: "Mã nhận của bạn");
        return mailSender.SendAsync(mail.ToEmail, mail.FullName, subject, text, html, ct);
    }
}
