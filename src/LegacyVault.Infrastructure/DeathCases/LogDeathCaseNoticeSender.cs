using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Bản gửi thông báo cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật,
/// và coi như đã giao để thử luồng 72 giờ trên máy dev. Không ghi email hay họ tên (OPS-04).
/// </summary>
public sealed class LogDeathCaseNoticeSender : IDeathCaseNoticeSender
{
    private readonly ILogger<LogDeathCaseNoticeSender> logger;

    public LogDeathCaseNoticeSender(ILogger<LogDeathCaseNoticeSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(DeathCaseOwnerNotice notice, CancellationToken ct)
    {
        logger.LogInformation("[THÔNG BÁO HỒ SƠ CHỨNG TỬ] Thông báo {NoticeId}, hồ sơ {CaseCode}; chưa cấu hình email nên không gửi thư.",
            notice.NoticeId, notice.CaseCode);
        return Task.CompletedTask;
    }
}
