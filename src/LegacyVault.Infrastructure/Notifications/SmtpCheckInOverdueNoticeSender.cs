using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Configuration;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Gửi thật qua SMTP thư "chủ kho quá hạn điểm danh, cần bạn kiểm tra" cho Executor (DMS-05).
/// Link về trang Executor của đúng kho; không có tên hay thông tin Owner.
/// </summary>
public sealed class SmtpCheckInOverdueNoticeSender : ICheckInOverdueNoticeSender
{
    private const string DefaultClientUrl = "http://localhost:5173";

    private readonly SmtpMailSender mailSender;
    private readonly string clientUrl;

    public SmtpCheckInOverdueNoticeSender(SmtpMailSender mailSender, IConfiguration configuration)
    {
        this.mailSender = mailSender;
        string? configured = configuration["App:ClientUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = DefaultClientUrl;
        }
        clientUrl = configured.TrimEnd('/');
    }

    public Task SendAsync(CheckInOverdueNotice notice, CancellationToken ct)
    {
        string link = clientUrl + "/executor?vault=" + notice.VaultId.ToString();
        EmailContent email = ExecutorOverdueNoticeEmail.Build(notice.FullName, link, false);
        return mailSender.SendAsync(notice.ToEmail, notice.FullName, email, ct);
    }
}
