using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Bản gửi thư báo kích hoạt cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật.
/// Không ghi email hay số điện thoại vào log.
/// </summary>
public sealed class LogPlanActivatedNoticeSender : IPlanActivatedNoticeSender
{
    private readonly ILogger<LogPlanActivatedNoticeSender> logger;

    public LogPlanActivatedNoticeSender(ILogger<LogPlanActivatedNoticeSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(PlanActivatedNotice notice, CancellationToken ct)
    {
        logger.LogInformation("[THƯ BÁO KÍCH HOẠT] Chưa cấu hình email nên không gửi thư cho người nhận.");
        return Task.CompletedTask;
    }
}
