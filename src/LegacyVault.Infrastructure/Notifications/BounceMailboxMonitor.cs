using LegacyVault.Application.Notifications;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Vòng lặp nền đọc hộp thư gửi (cùng tài khoản SMTP) qua IMAP, tìm thư báo lỗi "không giao được" rồi ghi vào EmailBounces.
/// Cấu hình: Email:Imap:Enabled (mặc định false), Email:Imap:Host (mặc định imap.gmail.com), Email:Imap:PollSeconds (mặc định 120).
/// Thiếu tài khoản SMTP hoặc chưa bật thì không làm gì.
/// </summary>
public sealed class BounceMailboxMonitor(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    IOptions<SmtpSettings> smtp,
    TimeProvider time,
    ILogger<BounceMailboxMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string user = smtp.Value.ResolveUserName();
        string password = smtp.Value.ResolvePassword();
        if (!config.GetValue("Email:Imap:Enabled", false) || user == string.Empty || password == string.Empty)
        {
            return;
        }
        int pollSeconds = Math.Max(30, config.GetValue("Email:Imap:PollSeconds", 120));
        string host = config["Email:Imap:Host"] ?? "imap.gmail.com";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int recorded = await PollAsync(host, user, password, stoppingToken);
                if (recorded > 0)
                {
                    logger.LogInformation("Bounce: ghi nhận {Count} email không giao được.", recorded);
                }
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Không log message: có thể chứa tên máy chủ hoặc tài khoản.
                logger.LogWarning("Bounce: đọc hộp thư IMAP lỗi ({ExceptionType}).", ex.GetType().Name);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<int> PollAsync(string host, string user, string password, CancellationToken ct)
    {
        using ImapClient client = new();
        await client.ConnectAsync(host, 993, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(user, password, ct);
        IMailFolder inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        IList<UniqueId> ids = await inbox.SearchAsync(
            SearchQuery.NotSeen.And(SearchQuery.FromContains("mailer-daemon").Or(SearchQuery.FromContains("postmaster"))), ct);
        int recorded = 0;
        foreach (UniqueId id in ids)
        {
            MimeMessage message = await inbox.GetMessageAsync(id, ct);
            foreach (BounceMessageParser.FailedRecipient failed in BounceMessageParser.Parse(message))
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IEmailBounceStore>()
                    .RecordAsync(failed.Email, failed.Code, time.GetUtcNow(), ct);
                recorded++;
            }
            await inbox.AddFlagsAsync(id, MessageFlags.Seen, true, ct);
        }
        await client.DisconnectAsync(true, ct);
        return recorded;
    }
}
