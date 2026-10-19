using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Bản gửi thư báo Executor cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật.
/// Không ghi email hay họ tên.
/// </summary>
public sealed class LogDeathCaseExecutorNoticeSender : IDeathCaseExecutorNoticeSender
{
    private readonly ILogger<LogDeathCaseExecutorNoticeSender> logger;

    public LogDeathCaseExecutorNoticeSender(ILogger<LogDeathCaseExecutorNoticeSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(DeathCaseExecutorNotice notice, CancellationToken ct)
    {
        logger.LogInformation("[THƯ BÁO EXECUTOR] Hồ sơ {CaseCode}, quyết định {Kind}; chưa cấu hình email nên không gửi thư.",
            notice.CaseCode, notice.Kind);
        return Task.CompletedTask;
    }
}
