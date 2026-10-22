using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Dịch vụ email dùng chung: gửi một thư qua SMTP (Gmail) bằng MailKit. Các bộ gửi theo nghiệp vụ
/// (xác minh email, nhắc điểm danh) chỉ dựng nội dung rồi gọi lớp này.
/// </summary>
/// <remarks>
/// - Mỗi lần gửi mở một kết nối riêng (lưu lượng thấp, tránh giữ kết nối treo với Gmail).
/// - Gửi lỗi thì ném exception nguyên vẹn để nơi gọi quyết định (job nhắc điểm danh giữ lời nhắc ở trạng thái chờ và thử lại).
/// - Không ghi log nội dung thư, địa chỉ người nhận hay mật khẩu (OPS-04, SEC-06).
/// - Không có UserName thì bỏ qua đăng nhập (dùng cho máy chủ SMTP nội bộ/test).
/// </remarks>
public sealed class SmtpMailSender
{
    private readonly SmtpSettings settings;

    public SmtpMailSender(IOptions<SmtpSettings> options)
    {
        settings = options.Value;
    }

    public async Task SendAsync(
        string toEmail, string toName, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        SecureSocketOptions? secureSocket = settings.ParseSecureSocket();
        if (secureSocket == null)
        {
            throw new InvalidOperationException("Email:Smtp:SecureSocket không hợp lệ.");
        }

        MimeMessage message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.ResolveFromAddress()));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;

        BodyBuilder body = new BodyBuilder();
        body.TextBody = textBody;
        body.HtmlBody = htmlBody;
        AttachLogoIfReferenced(body, htmlBody);
        message.Body = body.ToMessageBody();

        using (SmtpClient client = new SmtpClient())
        {
            client.Timeout = settings.TimeoutSeconds * 1000;
            await client.ConnectAsync(settings.Host, settings.Port, secureSocket.Value, ct);
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                await client.AuthenticateAsync(settings.UserName, settings.Password, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
    }

    /// <summary>Đính logo nội tuyến để Gmail và Outlook hiện ngay, không cần người nhận bấm "tải ảnh".</summary>
    private static void AttachLogoIfReferenced(BodyBuilder body, string htmlBody)
    {
        if (!htmlBody.Contains("cid:" + LegacyVaultEmailTheme.LogoContentId, StringComparison.Ordinal))
        {
            return;
        }
        using (Stream? stream = typeof(SmtpMailSender).Assembly.GetManifestResourceStream(LegacyVaultEmailTheme.LogoResourceName))
        {
            if (stream == null)
            {
                return;
            }
            MimeEntity logo = body.LinkedResources.Add("email-logo.png", stream, new ContentType("image", "png"));
            logo.ContentId = LegacyVaultEmailTheme.LogoContentId;
        }
    }
}
