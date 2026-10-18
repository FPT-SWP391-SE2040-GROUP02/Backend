using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Ghi một dòng log lúc API khởi động cho biết email đang gửi thật qua SMTP hay chỉ ghi log,
/// để lỗi "không nhận được thư" truy ra ngay (chưa đặt UserName/Password, hoặc đang chạy bản API cũ).
/// </summary>
/// <remarks>Không ghi UserName, Password hay địa chỉ email nào vào log (OPS-04).</remarks>
public sealed class EmailStartupLogger : IHostedService
{
    private readonly SmtpSettings settings;
    private readonly ILogger<EmailStartupLogger> logger;

    public EmailStartupLogger(IOptions<SmtpSettings> options, ILogger<EmailStartupLogger> logger)
    {
        settings = options.Value;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (settings.IsConfigured)
        {
            logger.LogInformation("Email: gửi thư thật qua SMTP {Host}:{Port} ({SecureSocket}).",
                settings.Host, settings.Port, settings.SecureSocket);
        }
        else
        {
            logger.LogWarning("Email: chưa cấu hình, chỉ ghi log và KHÔNG gửi thư thật. "
                + "Đặt Email:Smtp:UserName và Email:Smtp:Password (xem README mục 11) rồi khởi động lại API.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
