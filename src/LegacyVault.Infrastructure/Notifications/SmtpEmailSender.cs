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
        const string note = "Liên kết chỉ có hiệu lực trong vòng 24 giờ và dùng một lần duy nhất. Nếu bạn không đăng ký tài khoản LegacyVault, vui lòng bỏ qua email này.";

        string text = $"Xin chào {fullName},\n\nBấm vào liên kết sau để xác minh email tài khoản của bạn:\n{verificationUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Xác nhận địa chỉ email của bạn",
            fullName,
            "Cảm ơn bạn đã đăng ký tài khoản tại LegacyVault. Vui lòng nhấn vào nút bên dưới để hoàn tất việc xác minh email và bắt đầu bảo vệ di sản số của bạn.",
            verificationUrl,
            "Xác minh email ngay",
            note);

        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }

    public Task SendPasswordResetAsync(string toEmail, string fullName, string resetUrl, CancellationToken ct)
    {
        const string subject = "Đặt lại mật khẩu tài khoản LegacyVault";
        const string note = "Liên kết có hiệu lực trong 30 phút và chỉ dùng một lần. Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này; mật khẩu hiện tại vẫn an toàn.";

        string text = $"Xin chào {fullName},\n\nBấm vào liên kết sau để đặt lại mật khẩu mới:\n{resetUrl}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            "Yêu cầu đặt lại mật khẩu",
            fullName,
            "Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản LegacyVault của bạn. Hãy nhấn vào nút bên dưới để tiến hành thiết lập mật khẩu mới an toàn.",
            resetUrl,
            "Đặt lại mật khẩu",
            note);

        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }
}
