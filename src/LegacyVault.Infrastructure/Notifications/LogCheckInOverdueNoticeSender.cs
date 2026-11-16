using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Chưa cấu hình email: Development chỉ ghi log; môi trường khác ném lỗi, không giả vờ đã gửi.
/// Không ghi email hay tên người nhận vào log (OPS-04).
/// </summary>
public sealed class LogCheckInOverdueNoticeSender : ICheckInOverdueNoticeSender
{
    private readonly ILogger<LogCheckInOverdueNoticeSender> logger;
    private readonly IHostEnvironment environment;

    public LogCheckInOverdueNoticeSender(ILogger<LogCheckInOverdueNoticeSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(CheckInOverdueNotice notice, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được thư báo quá hạn điểm danh.");
        }
        logger.LogInformation(
            "[QUÁ HẠN ĐIỂM DANH] Kho {VaultId}, hạn {DueAt:O}; chưa cấu hình email nên không gửi thư.",
            notice.VaultId, notice.DueAt);
        return Task.CompletedTask;
    }
}
