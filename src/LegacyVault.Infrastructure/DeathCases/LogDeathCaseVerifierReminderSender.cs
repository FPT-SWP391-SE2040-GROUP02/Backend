using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.DeathCases;

/// <summary>
/// Bản gửi thư nhắc Verifier cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật,
/// và coi như đã nhắc để thử luồng giao lại trên máy dev. Không ghi email hay họ tên (OPS-04).
/// </summary>
public sealed class LogDeathCaseVerifierReminderSender : IDeathCaseVerifierReminderSender
{
    private readonly ILogger<LogDeathCaseVerifierReminderSender> logger;

    public LogDeathCaseVerifierReminderSender(ILogger<LogDeathCaseVerifierReminderSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(DeathCaseVerifierReminder reminder, CancellationToken ct)
    {
        logger.LogInformation("[NHẮC VERIFIER HỒ SƠ CHỨNG TỬ] Phân công {AssignmentId}, hồ sơ {CaseCode}; chưa cấu hình email nên không gửi thư.",
            reminder.AssignmentId, reminder.CaseCode);
        return Task.CompletedTask;
    }
}
