using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Bản gửi thông báo cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật,
/// và chỉ ở Development mới coi như đã giao để thử luồng 72 giờ trên máy dev. Không ghi email hay họ tên (OPS-04).
/// </summary>
public sealed class LogDeathCaseNoticeSender : IDeathCaseNoticeSender
{
    private readonly ILogger<LogDeathCaseNoticeSender> logger;
    private readonly IHostEnvironment environment;

    public LogDeathCaseNoticeSender(ILogger<LogDeathCaseNoticeSender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(DeathCaseOwnerNotice notice, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            // Ngoài Development, không có email thật thì KHÔNG được coi là đã giao: ném lỗi để thông báo giữ Pending,
            // không bắt đầu đồng hồ 72 giờ (DEATH-08). Hết số lần thử thì sang Failed.
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được thông báo hồ sơ chứng tử.");
        }
        logger.LogInformation("[THÔNG BÁO HỒ SƠ CHỨNG TỬ] Thông báo {NoticeId}, hồ sơ {CaseCode}; chưa cấu hình email nên không gửi thư.",
            notice.NoticeId, notice.CaseCode);
        return Task.CompletedTask;
    }
}
