using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>
/// Bản gửi khóa cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật.
/// Ở Development, log in khóa để thử tay; môi trường khác không ghi khóa hay email và báo lỗi (SEC-07).
/// </summary>
public sealed class LogHandoverKeySender : IHandoverKeySender
{
    private readonly ILogger<LogHandoverKeySender> logger;
    private readonly IHostEnvironment environment;

    public LogHandoverKeySender(ILogger<LogHandoverKeySender> logger, IHostEnvironment environment)
    {
        this.logger = logger;
        this.environment = environment;
    }

    public Task SendAsync(HandoverKeyMail mail, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            logger.LogWarning("[KHÓA MỞ GÓI] Lượt {TurnId}; chưa cấu hình email nên không gửi thư.", mail.TurnId);
            throw new InvalidOperationException("Chưa cấu hình Email:Smtp nên không gửi được khóa.");
        }
        logger.LogInformation("[KHÓA MỞ GÓI] Lượt {TurnId}; khóa gửi người nhận: {Key}", mail.TurnId, mail.Key);
        return Task.CompletedTask;
    }
}
