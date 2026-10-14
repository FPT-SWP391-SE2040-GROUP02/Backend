using LegacyVault.Application.Notifications;
using LegacyVault.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Notifications;

/// <summary>
/// Bản gửi nhắc điểm danh dành cho môi trường dev: chỉ ghi log, không gửi email thật.
/// Khi có nhà cung cấp email, thay bằng bản gửi thật (đăng ký thay trong InfrastructureConfig).
/// </summary>
public sealed class LogCheckInReminderSender : ICheckInReminderSender
{
    private readonly ILogger<LogCheckInReminderSender> logger;

    public LogCheckInReminderSender(ILogger<LogCheckInReminderSender> logger)
    {
        this.logger = logger;
    }

    public Task SendReminderAsync(
        string toEmail, string fullName, CheckInReminderKind kind,
        DateTimeOffset dueAt, DateTimeOffset graceEndsAt, CancellationToken ct)
    {
        // Không ghi email đầy đủ vào log (OPS-04).
        logger.LogInformation(
            "[NHẮC ĐIỂM DANH] Mốc {Kind}; hạn điểm danh {DueAt:O}; hết thời gian chờ {GraceEndsAt:O}",
            kind, dueAt, graceEndsAt);
        return Task.CompletedTask;
    }
}
