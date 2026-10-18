using System.Net;
using LegacyVault.Application.Notifications;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>Gửi email xác minh tài khoản thật qua Gmail (SmtpMailSender). Không ghi link/token vào log (SEC-06).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpMailSender mailSender;

    public SmtpEmailSender(SmtpMailSender mailSender)
    {
        this.mailSender = mailSender;
    }

    public Task SendEmailVerificationAsync(string toEmail, string fullName, string verificationUrl, CancellationToken ct)
    {
        const string subject = "Xác minh email tài khoản LegacyVault";
        const string note = "Liên kết chỉ dùng được một lần. Nếu bạn không đăng ký tài khoản LegacyVault, hãy bỏ qua email này.";

        string text = $"Xin chào {fullName},\n\nBấm vào liên kết sau để xác minh email của bạn:\n{verificationUrl}\n\n{note}\n";
        string html = $"<p>Xin chào {WebUtility.HtmlEncode(fullName)},</p>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(verificationUrl)}\">Xác minh email của bạn</a></p>"
            + $"<p><small>{WebUtility.HtmlEncode(note)}</small></p>";
        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }

    public Task SendPasswordResetAsync(string toEmail, string fullName, string resetUrl, CancellationToken ct)
    {
        const string subject = "Đặt lại mật khẩu LegacyVault";
        const string note = "Liên kết có hạn 30 phút và chỉ dùng được một lần. Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này; mật khẩu hiện tại vẫn giữ nguyên.";

        string text = $"Xin chào {fullName},\n\nBấm vào liên kết sau để đặt mật khẩu mới:\n{resetUrl}\n\n{note}\n";
        string html = $"<p>Xin chào {WebUtility.HtmlEncode(fullName)},</p>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(resetUrl)}\">Đặt mật khẩu mới</a></p>"
            + $"<p><small>{WebUtility.HtmlEncode(note)}</small></p>";
        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }
}
