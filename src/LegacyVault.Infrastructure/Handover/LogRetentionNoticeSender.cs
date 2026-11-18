using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Chưa cấu hình email: Development chỉ ghi log; môi trường khác ném lỗi, không giả vờ đã gửi.</summary>
public sealed class LogRetentionNoticeSender : IRetentionNoticeSender
{
    private readonly ILogger<LogRetentionNoticeSender> logger;
    private readonly IHostEnvironment environment;

    public LogRetentionNoticeSender(ILogger<LogRetentionNoticeSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(RetentionNotice notice, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được thư mốc lưu dữ liệu.");
        }
        logger.LogInformation("[MỐC LƯU DỮ LIỆU] Lượt {TurnId}, mốc {Stage}; chưa cấu hình email nên không gửi thư.",
            notice.TurnId, notice.Stage);
        return Task.CompletedTask;
    }
}
