using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Dịch vụ email dùng chung: hỗ trợ gửi qua Webhook (Google Apps Script)
/// hoặc SMTP trực tiếp (Gmail qua MailKit).
/// </summary>
/// <remarks>
/// Máy chủ, tài khoản, mật khẩu... đọc từ cấu hình đang có hiệu lực mỗi lần gửi (Admin sửa trên web đè appsettings).
/// Ô chính gửi lỗi thì thử ô dự phòng nếu Admin đã bật; cả hai lỗi thì ném lỗi của ô chính như trước đây.
/// Việc chọn gửi thư thật hay chỉ ghi log vẫn quyết định lúc khởi động theo appsettings (xem EmailConfig).
/// </remarks>
public class SmtpMailSender
{
    private static readonly SystemConfigSlot[] Slots = { SystemConfigSlot.Primary, SystemConfigSlot.Fallback };

    private readonly IServiceScopeFactory scopes;
    private readonly IServiceStatusTracker tracker;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SmtpMailSender>? logger;

    public SmtpMailSender(
        IServiceScopeFactory scopes,
        IServiceStatusTracker tracker,
        TimeProvider timeProvider,
        ILogger<SmtpMailSender>? logger = null)
    {
        this.scopes = scopes;
        this.tracker = tracker;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Gửi một thư đã dựng; loại thư Admin đã tắt thì bỏ qua, không gửi và không báo lỗi.</summary>
    public Task SendAsync(string toEmail, string toName, EmailContent content, CancellationToken ct)
    {
        if (content.TemplateKey != null && !LegacyVault.Application.Notifications.EmailTemplateOverrides.IsEnabled(content.TemplateKey))
        {
            return Task.CompletedTask;
        }
        return SendAsync(toEmail, toName, content.Subject, content.TextBody, content.HtmlBody, ct);
    }

    public async Task SendAsync(
        string toEmail, string toName, string subject, string textBody, string htmlBody, CancellationToken ct)
    {
        Exception? firstFailure = null;
        foreach (SystemConfigSlot slot in Slots)
        {
            ResolvedConfig? config = await ResolveAsync(slot, ct);
            if (config == null)
            {
                continue;
            }
            SmtpSettings settings = SmtpSettingsMapping.FromConfig(config);
            try
            {
                await DeliverAsync(settings, toEmail, toName, subject, textBody, htmlBody, ct);
                Record(slot, true, null);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Record(slot, false, ErrorCodeOf(ex));
                logger?.LogWarning("Email: Ô {Slot} gửi thư không được ({ErrorType}).", slot, ex.GetType().Name);
                if (firstFailure == null)
                {
                    firstFailure = ex;
                }
            }
        }

        if (firstFailure != null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
        throw new InvalidOperationException("Chưa có cấu hình email nào dùng được.");
    }

    /// <summary>Gửi một thư bằng đúng một bộ cấu hình: Webhook nếu có địa chỉ, không thì SMTP trực tiếp. Tách ra để test thay thế được.</summary>
    protected virtual async Task DeliverAsync(
        SmtpSettings settings, string toEmail, string toName, string subject, string textBody, string htmlBody,
        CancellationToken ct)
    {
        // 1. Ưu tiên gửi qua Google Apps Script Webhook (Port 443 HTTPS - không bao giờ bị chặn firewall)
        string webhookUrl = settings.ResolveWebhookUrl();
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            await SendViaWebhookAsync(settings, webhookUrl, toEmail, toName, subject, textBody, htmlBody, ct);
            return;
        }

        // 2. Fallback gửi qua giao thức SMTP thông thường (MailKit)
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
        SmtpSettings settings, string webhookUrl, string toEmail, string toName, string subject, string textBody,
        string htmlBody, CancellationToken ct)
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

    /// <summary>Đọc cấu hình của ô; lỗi đọc (DB, giải mã) coi như ô không dùng được và không ném ra ngoài.</summary>
    private async Task<ResolvedConfig?> ResolveAsync(SystemConfigSlot slot, CancellationToken ct)
    {
        try
        {
            using (IServiceScope scope = scopes.CreateScope())
            {
                ISystemConfigResolver resolver = scope.ServiceProvider.GetRequiredService<ISystemConfigResolver>();
                return await resolver.ResolveAsync(SystemConfigServices.Smtp, slot, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning("Email: Không đọc được cấu hình ô {Slot} ({ErrorType}).", slot, ex.GetType().Name);
            return null;
        }
    }

    private void Record(SystemConfigSlot slot, bool succeeded, string? errorCode)
    {
        tracker.Record(SystemConfigServices.Smtp, slot, succeeded, errorCode, timeProvider.GetUtcNow());
    }

    /// <summary>Mã lỗi ngắn nói loại sự cố (không kèm chi tiết, không bao giờ có mật khẩu).</summary>
    internal static string ErrorCodeOf(Exception ex)
    {
        if (ex is AuthenticationException)
        {
            return "AUTH";
        }
        if (ex is TimeoutException || ex is OperationCanceledException)
        {
            return "TIMEOUT";
        }
        SmtpCommandException? command = ex as SmtpCommandException;
        if (command != null)
        {
            return "SMTP_" + (int)command.StatusCode;
        }
        if (ex is SocketException || ex is IOException || ex is HttpRequestException)
        {
            return "NETWORK";
        }
        return "ERROR";
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
