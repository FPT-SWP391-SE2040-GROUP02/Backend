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
            note,
            "Xác minh email");

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
            note,
            "Đặt lại mật khẩu",
            true);

        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }

    public Task SendVaultPinNoticeAsync(
        string toEmail, string fullName, VaultPinNoticeKind kind, DateTimeOffset? lockedUntil, CancellationToken ct)
    {
        string subject;
        string kicker;
        string lead;
        switch (kind)
        {
            case VaultPinNoticeKind.Set:
                subject = "Mã PIN kho LegacyVault vừa được đặt";
                kicker = "Mã PIN của kho";
                lead = "Mã PIN của kho LegacyVault vừa được đặt.\nTừ giờ, các thao tác nhạy cảm như tải nội dung tài sản, xóa tài sản hoặc đổi người nhận đều cần nhập mã PIN này.";
                break;
            case VaultPinNoticeKind.Changed:
                subject = "Mã PIN kho LegacyVault vừa được đổi";
                kicker = "Mã PIN của kho";
                lead = "Mã PIN của kho LegacyVault vừa được đổi bằng mã PIN cũ.";
                break;
            case VaultPinNoticeKind.Reset:
                subject = "Mã PIN kho LegacyVault vừa được đặt lại";
                kicker = "Mã PIN của kho";
                lead = "Mã PIN của kho LegacyVault vừa được đặt lại bằng mật khẩu tài khoản của bạn.";
                break;
            default:
                subject = "Mã PIN kho LegacyVault đang bị khóa tạm";
                kicker = "Cảnh báo bảo mật";
                lead = "Mã PIN của kho LegacyVault vừa bị nhập sai nhiều lần liên tiếp nên đã bị khóa tạm.";
                if (lockedUntil.HasValue)
                {
                    // Giờ Việt Nam (UTC+07:00) để người đọc không phải tự quy đổi.
                    DateTimeOffset local = lockedUntil.Value.ToOffset(TimeSpan.FromHours(7));
                    lead += $"\nPIN sẽ được mở khóa lúc {local:HH:mm 'ngày' dd/MM/yyyy} (giờ Việt Nam).";
                }
                break;
        }

        const string note = "Nếu đây không phải bạn, hãy đổi mật khẩu tài khoản ngay và đặt lại mã PIN của kho. Email này không chứa mã PIN.";
        string text = $"Xin chào {fullName},\n\n{lead}\n\n{note}\n";
        string html = LegacyVaultEmailTheme.BuildHtml(
            subject, fullName, lead, string.Empty, string.Empty, note, kicker, true);

        return mailSender.SendAsync(toEmail, fullName, subject, text, html, ct);
    }
}
