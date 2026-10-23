using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Dịch vụ email dùng chung: gửi một thư qua SMTP (Gmail) bằng MailKit. Các bộ gửi theo nghiệp vụ
/// (xác minh email, nhắc điểm danh) chỉ dựng nội dung rồi gọi lớp này.
/// </summary>
public sealed class SmtpMailSender
{
    private readonly SmtpSettings settings;
    private readonly ILogger<SmtpMailSender>? logger;

    public SmtpMailSender(IOptions<SmtpSettings> options, ILogger<SmtpMailSender>? logger = null)
    {
        settings = options.Value;
        this.logger = logger;
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
            try
            {
                logger?.LogInformation("SMTP: Đang kết nối tới {Host}:{Port} ({SecureSocket}) để gửi thư '{Subject}' tới {ToEmail}...",
                    settings.Host, settings.Port, secureSocket.Value, subject, toEmail);

                try
                {
                    await client.ConnectAsync(settings.Host, settings.Port, secureSocket.Value, ct);
                }
                catch (Exception ex) when (settings.Host.Equals("smtp.gmail.com", StringComparison.OrdinalIgnoreCase) && settings.Port == 587)
                {
                    logger?.LogWarning("SMTP: Kết nối 587 gặp lỗi ({Message}), tự động chuyển sang cổng 465 SSL...", ex.Message);
                    await client.ConnectAsync("smtp.gmail.com", 465, SecureSocketOptions.SslOnConnect, ct);
                }

                if (!string.IsNullOrEmpty(settings.UserName))
                {
                    await client.AuthenticateAsync(settings.UserName, settings.ResolvePassword(), ct);
                }
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
                logger?.LogInformation("SMTP: Gửi email '{Subject}' tới {ToEmail} THÀNH CÔNG!", subject, toEmail);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "SMTP: Lỗi khi gửi email tới {ToEmail}: {Message}", toEmail, ex.Message);
                throw;
            }
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
