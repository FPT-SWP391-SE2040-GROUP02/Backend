using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Dịch vụ email dùng chung: hỗ trợ gửi qua Webhook (Google Apps Script), Brevo HTTPS API,
/// hoặc SMTP trực tiếp (Gmail qua MailKit).
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
        // 1. Ưu tiên gửi qua Google Apps Script Webhook (Port 443 HTTPS - không bao giờ bị chặn firewall)
        string webhookUrl = settings.ResolveWebhookUrl();
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            await SendViaWebhookAsync(webhookUrl, toEmail, toName, subject, textBody, htmlBody, ct);
            return;
        }

        // 2. Ưu tiên gửi qua Brevo HTTPS API (Port 443 HTTPS - cho phép người gửi @gmail.com miễn phí)
        string brevoKey = settings.ResolveBrevoApiKey();
        if (!string.IsNullOrWhiteSpace(brevoKey))
        {
            await SendViaBrevoApiAsync(brevoKey, toEmail, toName, subject, textBody, htmlBody, ct);
            return;
        }

        // 3. Fallback gửi qua giao thức SMTP thông thường (MailKit)
        SecureSocketOptions? secureSocket = settings.ParseSecureSocket();
        if (secureSocket == null)
        {
            throw new InvalidOperationException("Email:Smtp:SecureSocket không hợp lệ.");
        }

        string host = settings.ResolveHost();
        string userName = settings.ResolveUserName();
        string password = settings.ResolvePassword();
        string fromAddress = settings.ResolveFromAddress();
        string fromName = (settings.FromName ?? "LegacyVault").Trim().Trim('"').Trim('\'');

        MimeMessage message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
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
            // Bỏ qua kiểm tra chứng chỉ thu hồi trên môi trường container/Linux để tránh timeout mạng
            client.CheckCertificateRevocation = false;
            try
            {
                logger?.LogInformation("SMTP: Đang kết nối tới {Host}:{Port} ({SecureSocket}) để gửi thư '{Subject}' tới {ToEmail}...",
                    host, settings.Port, secureSocket.Value, subject, toEmail);

                try
                {
                    await client.ConnectAsync(host, settings.Port, secureSocket.Value, ct);
                }
                catch (Exception ex) when (host.Equals("smtp.gmail.com", StringComparison.OrdinalIgnoreCase))
                {
                    int fallbackPort = settings.Port == 465 ? 587 : 465;
                    var fallbackSocket = fallbackPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                    logger?.LogWarning("SMTP: Kết nối tới {Host}:{Port} gặp lỗi ({Message}), tự động chuyển sang cổng {FallbackPort} ({FallbackSocket})...",
                        host, settings.Port, ex.Message, fallbackPort, fallbackSocket);
                    await client.ConnectAsync(host, fallbackPort, fallbackSocket, ct);
                }

                if (!string.IsNullOrEmpty(userName))
                {
                    await client.AuthenticateAsync(userName, password, ct);
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

    private async Task SendViaWebhookAsync(
        string webhookUrl, string toEmail, string toName, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        logger?.LogInformation("Email: Đang gửi thư '{Subject}' tới {ToEmail} qua Webhook HTTPS ({WebhookUrl})...",
            subject, toEmail, webhookUrl);

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            CheckCertificateRevocationList = false
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 25)
        };

        var payload = new
        {
            to = toEmail,
            toName = toName,
            subject = subject,
            text = textBody,
            html = htmlBody,
            fromName = (settings.FromName ?? "LegacyVault").Trim().Trim('"').Trim('\''),
            fromEmail = settings.ResolveFromAddress()
        };

        var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
        // Google Apps Script trả 200 hoặc 302 sau khi gửi thư thành công
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Redirect && response.StatusCode != System.Net.HttpStatusCode.Moved)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            logger?.LogError("Email: Gửi qua Webhook thất bại với HTTP {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Webhook gửi email thất bại: HTTP {(int)response.StatusCode} - {errorBody}");
        }

        logger?.LogInformation("Email: Gửi thư '{Subject}' tới {ToEmail} qua Webhook THÀNH CÔNG!", subject, toEmail);
    }

    private async Task SendViaBrevoApiAsync(
        string apiKey, string toEmail, string toName, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        logger?.LogInformation("Email: Đang gửi thư '{Subject}' tới {ToEmail} qua Brevo HTTPS API...", subject, toEmail);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 25)
        };
        client.DefaultRequestHeaders.Add("api-key", apiKey);

        var payload = new
        {
            sender = new
            {
                name = (settings.FromName ?? "LegacyVault").Trim().Trim('"').Trim('\''),
                email = settings.ResolveFromAddress()
            },
            to = new[]
            {
                new { email = toEmail, name = string.IsNullOrWhiteSpace(toName) ? toEmail : toName }
            },
            subject = subject,
            htmlContent = htmlBody,
            textContent = textBody
        };

        var response = await client.PostAsJsonAsync("https://api.brevo.com/v3/smtp/email", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);
            logger?.LogError("Email: Gửi qua Brevo API thất bại với HTTP {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Brevo API gửi email thất bại: HTTP {(int)response.StatusCode} - {errorBody}");
        }

        logger?.LogInformation("Email: Gửi thư '{Subject}' tới {ToEmail} qua Brevo API THÀNH CÔNG!", subject, toEmail);
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
