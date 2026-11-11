using LegacyVault.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Handover;

/// <summary>Bản gửi thư báo biên bản cho môi trường chưa cấu hình email: chỉ ghi log, không gửi thư thật. Không ghi email hay họ tên vào log.</summary>
public sealed class LogHandoverDocumentNoticeSender : IHandoverDocumentNoticeSender
{
    private readonly ILogger<LogHandoverDocumentNoticeSender> logger;

    public LogHandoverDocumentNoticeSender(ILogger<LogHandoverDocumentNoticeSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(HandoverDocumentNotice notice, CancellationToken ct)
    {
        logger.LogInformation("[THƯ BÁO BIÊN BẢN] Chưa cấu hình email nên không gửi thư. Loại {Kind}, mã {Code}.", notice.Kind, notice.DocumentCode);
        return Task.CompletedTask;
    }
}
